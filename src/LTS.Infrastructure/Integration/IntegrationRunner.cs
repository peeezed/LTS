using System.Text.Json;
using LTS.Application.Abstractions;
using LTS.Application.Integration;
using LTS.Application.Security;
using LTS.Application.Tracking;
using LTS.Domain.Entities;
using LTS.Domain.Enums;
using LTS.Domain.Kpi;
using LTS.Domain.Services;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LTS.Infrastructure.Integration;

/// <summary>
/// Runs one poll of one integration source end to end: fetch, normalise, map raw status codes
/// onto LTS milestones, apply them, and record what happened. This is the only place that
/// knows how an external payload becomes tracked data.
/// </summary>
public sealed class IntegrationRunner(
    LtsDbContext db,
    IIntegrationAdapterRegistry adapters,
    IMilestoneService milestones,
    IKpiTargetProvider kpiTargets,
    IClock clock,
    ILogger<IntegrationRunner> logger)
{
    public async Task<IntegrationRun> RunAsync(int sourceId, CancellationToken cancellationToken = default)
    {
        var source = await db.IntegrationSources
            .Include(s => s.Country)
            .FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken)
            ?? throw new InvalidOperationException($"Integration source {sourceId} does not exist.");

        var run = new IntegrationRun
        {
            IntegrationSourceId = source.Id,
            StartedAt = clock.UtcNow,
            Status = IntegrationRunStatus.Running
        };

        db.IntegrationRuns.Add(run);
        source.LastRunAt = run.StartedAt;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var adapter = adapters.Find(source.AdapterKey)
                ?? throw new InvalidOperationException(
                    $"No adapter is registered for key '{source.AdapterKey}'. " +
                    $"Registered keys: {string.Join(", ", adapters.RegisteredKeys)}.");

            var context = new IntegrationContext(
                source.Id,
                source.CountryId,
                source.Country?.Code ?? string.Empty,
                source.BaseUrl,
                source.SecretName,
                source.SettingsJson,
                source.Cursor);

            var result = await adapter.FetchAsync(context, cancellationToken);
            run.MessagesReceived = result.TotalMessages;

            await ImportShipmentsAsync(run, source, result.Shipments, cancellationToken);
            await ApplyEventsAsync(run, source, result.Events, cancellationToken);

            // The cursor only advances on a run that got all the way through, so a failure
            // re-reads the same window rather than skipping it.
            if (result.Cursor is not null)
            {
                source.Cursor = result.Cursor;
            }

            source.LastSuccessAt = clock.UtcNow;
            run.Status = run.MessagesFailed > 0
                ? IntegrationRunStatus.PartiallySucceeded
                : IntegrationRunStatus.Succeeded;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Integration source {SourceId} ({Name}) failed.", source.Id, source.Name);
            run.Status = IntegrationRunStatus.Failed;
            run.ErrorMessage = exception.Message;
        }

        run.FinishedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return run;
    }

    /// <summary>
    /// Creates or updates shipments and their transfer splits from the source's master data.
    /// </summary>
    private async Task ImportShipmentsAsync(
        IntegrationRun run,
        IntegrationSource source,
        IReadOnlyList<ShipmentSnapshotDto> snapshots,
        CancellationToken cancellationToken)
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        var lookups = await ReferenceLookups.LoadAsync(db, cancellationToken);
        var resolver = await kpiTargets.GetResolverAsync(cancellationToken);

        foreach (var snapshot in snapshots)
        {
            var message = NewMessage(run, snapshot.ReferenceNo, null, JsonSerializer.Serialize(snapshot));

            try
            {
                var countryId = lookups.CountryId(snapshot.ArrivalCountryCode);
                if (countryId is null)
                {
                    Fail(run, message, $"Unknown arrival country '{snapshot.ArrivalCountryCode}'.");
                    continue;
                }

                // A source may only write into its own country, whatever its payload claims.
                if (countryId != source.CountryId)
                {
                    Fail(run, message,
                        $"Shipment is for country '{snapshot.ArrivalCountryCode}' but the source belongs to another country.");
                    continue;
                }

                var shipment = await db.Shipments
                    .Include(s => s.Transfers)
                    .Include(s => s.LoadingPoint)
                    .FirstOrDefaultAsync(s => s.ReferenceNo == snapshot.ReferenceNo, cancellationToken);

                if (shipment is null)
                {
                    shipment = new Shipment
                    {
                        ReferenceNo = snapshot.ReferenceNo,
                        InvoiceNo = snapshot.InvoiceNo,
                        ArrivalCountryId = countryId.Value
                    };

                    db.Shipments.Add(shipment);
                    run.ShipmentsCreated++;
                }
                else
                {
                    run.ShipmentsUpdated++;
                }

                shipment.InvoiceNo = snapshot.InvoiceNo;
                shipment.InvoiceDate = snapshot.InvoiceDate;
                shipment.ArrivalCustomsId = lookups.LookupId(LookupKind.ArrivalCustoms, snapshot.ArrivalCustomsCode);
                shipment.ExportTypeId = lookups.LookupId(LookupKind.ExportType, snapshot.ExportTypeCode);
                shipment.TransportTypeId = lookups.LookupId(LookupKind.TransportType, snapshot.TransportTypeCode);
                shipment.LoadingPointId = lookups.LoadingPointId(snapshot.LoadingPointCode);
                shipment.LogisticsCompanyId = lookups.PartnerId(PartnerType.LogisticsCompany, snapshot.LogisticsCompanyCode);
                shipment.BrokerId = lookups.PartnerId(PartnerType.Broker, snapshot.BrokerCode);

                if (shipment.LoadingPointId is { } loadingPointId)
                {
                    shipment.LoadingPoint = lookups.LoadingPoint(loadingPointId);
                }

                SyncTransfers(run, shipment, snapshot, lookups, countryId.Value);

                ShipmentRecalculator.Recalculate(shipment, resolver, clock.Today);

                message.Status = IntegrationMessageStatus.Processed;
                message.ProcessedAt = clock.UtcNow;
                run.MessagesProcessed++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Fail(run, message, exception.Message);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void SyncTransfers(
        IntegrationRun run,
        Shipment shipment,
        ShipmentSnapshotDto snapshot,
        ReferenceLookups lookups,
        int countryId)
    {
        foreach (var transferSnapshot in snapshot.Transfers)
        {
            var storeId = lookups.StoreId(countryId, transferSnapshot.StoreCode);
            if (storeId is null)
            {
                // An unknown store is reported rather than silently dropped: it usually means
                // the store master data has not been created yet.
                run.MessagesFailed++;
                continue;
            }

            var transferNo = Transfer.BuildTransferNo(shipment.ReferenceNo, transferSnapshot.StoreCode);
            var transfer = shipment.Transfers.FirstOrDefault(t => t.TransferNo == transferNo);

            if (transfer is null)
            {
                transfer = new Transfer
                {
                    ShipmentId = shipment.Id,
                    StoreId = storeId.Value,
                    TransferNo = transferNo
                };

                shipment.Transfers.Add(transfer);
                run.TransfersCreated++;
            }
            else
            {
                run.TransfersUpdated++;
            }

            transfer.TotalBoxes = transferSnapshot.TotalBoxes;
            transfer.TotalItems = transferSnapshot.TotalItems;
        }
    }

    /// <summary>
    /// Turns raw status codes into milestone dates using the source's status mappings — the
    /// step that makes one country's vocabulary indistinguishable from another's inside LTS.
    /// </summary>
    private async Task ApplyEventsAsync(
        IntegrationRun run,
        IntegrationSource source,
        IReadOnlyList<MilestoneEventDto> events,
        CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        var mappings = await db.StatusMappings
            .AsNoTracking()
            .Where(m => m.IntegrationSourceId == source.Id && m.IsActive)
            .ToDictionaryAsync(m => m.RawCode, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var changes = new List<MilestoneChange>();

        foreach (var integrationEvent in events)
        {
            var message = NewMessage(run, integrationEvent.Reference, integrationEvent.RawStatusCode,
                integrationEvent.RawPayload ?? JsonSerializer.Serialize(integrationEvent));
            message.ExternalId = integrationEvent.ExternalId;

            if (!mappings.TryGetValue(integrationEvent.RawStatusCode, out var mapping))
            {
                // Not an error: it is a prompt for an admin to decide what the code means.
                message.Status = IntegrationMessageStatus.Skipped;
                message.ErrorMessage = $"No status mapping exists for code '{integrationEvent.RawStatusCode}'.";
                run.UnmappedCodeCount++;
                continue;
            }

            if (mapping.IsIgnored || mapping.MilestoneType is null)
            {
                message.Status = IntegrationMessageStatus.Skipped;
                message.ErrorMessage = "Code is mapped as ignored.";
                continue;
            }

            changes.Add(new MilestoneChange(integrationEvent.Reference, mapping.MilestoneType.Value,
                integrationEvent.EventDate));

            message.Status = IntegrationMessageStatus.Processed;
            message.ProcessedAt = clock.UtcNow;
            run.MessagesProcessed++;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (changes.Count == 0)
        {
            return;
        }

        // The poller acts as the system: there is no user whose permissions could apply, and
        // the source's own precedence setting decides whether it may overwrite manual entries.
        var options = new MilestoneApplyOptions(
            MilestoneSource.Integration,
            EnforcePermissions: false,
            ManualOverrideWins: source.ManualOverrideWins,
            IntegrationRunId: run.Id,
            Note: source.Name);

        var result = await milestones.ApplyAsync(changes, options, UserPermissions.None, cancellationToken);

        run.MilestonesApplied = result.Applied;

        if (result.Errors.Count > 0)
        {
            run.MessagesFailed += result.Errors.Count;
            logger.LogWarning("Integration source {Source} produced {Count} rejected milestone(s): {Errors}",
                source.Name, result.Errors.Count,
                string.Join("; ", result.Errors.Take(5).Select(e => $"{e.Reference}: {e.Message}")));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private IntegrationMessage NewMessage(IntegrationRun run, string reference, string? rawCode, string payload)
    {
        var message = new IntegrationMessage
        {
            IntegrationRunId = run.Id,
            EntityReference = reference,
            RawStatusCode = rawCode,
            Payload = payload,
            ReceivedAt = clock.UtcNow
        };

        db.IntegrationMessages.Add(message);

        return message;
    }

    private static void Fail(IntegrationRun run, IntegrationMessage message, string error)
    {
        message.Status = IntegrationMessageStatus.Failed;
        message.ErrorMessage = error;
        run.MessagesFailed++;
    }
}
