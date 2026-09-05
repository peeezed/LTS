using System.Text.Json;
using LTS.Application.Abstractions;
using LTS.Application.RomaniaOneClick;
using LTS.Application.Security;
using LTS.Application.Tracking;
using LTS.Domain.Enums;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LTS.Infrastructure.RomaniaOneClick;

/// <summary>
/// Runs one full poll: every LTS transfer with a RomaniaPermShipmentId set and no recorded Store
/// Arrival yet gets looked up individually against KLG OneClick, and whatever transfer-scope dates
/// come back are applied through the same IIntegrationMilestoneService.ApplyAsync path
/// Shipment Details' manual entry uses - so status/KPI recompute and audit logging happen exactly
/// as they already do, with no separate code path to keep in sync. shipment_date is parsed but
/// deliberately never applied - it maps to the shipment-scope Crossdock Arrival milestone, which
/// this per-transfer feed leaves alone for now. Every lookup is staged into LTS_ShipmentFeedStaging,
/// the same table/lifecycle the internal shipment feed already uses. One transfer's failure never
/// stops the rest of the batch.
/// </summary>
public sealed class RomaniaShipmentFeedRunner(
    IDbContextFactory<LtsIntegrationDbContext> dbFactory,
    IRomaniaOneClickClient client,
    IIntegrationMilestoneService milestones,
    IClock clock,
    ILogger<RomaniaShipmentFeedRunner> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var pending = await LoadPendingTransfersAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        foreach (var (transferNo, permShipmentId) in pending)
        {
            await ProcessTransferAsync(db, transferNo, permShipmentId, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Every transfer linked to a KLG id whose Store Arrival date is not yet recorded - a plain
    /// dictionary lookup rather than a SQL join, since a brand-new transfer may not have an
    /// LTS_ShipmentTransferDates row at all yet (ApplyAsync creates one lazily on first write),
    /// which a straight join would silently exclude.
    /// </summary>
    private async Task<List<(string TransferNo, string PermShipmentId)>> LoadPendingTransfersAsync(
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var linked = await db.ShipmentTransfers.AsNoTracking()
            .Where(t => t.RomaniaPermShipmentId != null && t.RomaniaPermShipmentId != "")
            .Select(t => new { t.TransferNo, PermShipmentId = t.RomaniaPermShipmentId! })
            .ToListAsync(cancellationToken);

        if (linked.Count == 0)
        {
            return [];
        }

        var transferNos = linked.Select(t => t.TransferNo).ToList();
        var storeArrivalByTransferNo = await db.ShipmentTransferDates.AsNoTracking()
            .Where(d => transferNos.Contains(d.TransferNo))
            .ToDictionaryAsync(d => d.TransferNo, d => d.StoreArrivalDate, cancellationToken);

        return [.. linked
            .Where(t => storeArrivalByTransferNo.GetValueOrDefault(t.TransferNo) is null)
            .Select(t => (t.TransferNo, t.PermShipmentId))];
    }

    private async Task ProcessTransferAsync(
        LtsIntegrationDbContext db, string transferNo, string permShipmentId, CancellationToken cancellationToken)
    {
        var fetchedAt = clock.UtcNow;
        RomaniaDomesticShipmentDto? shipment;

        try
        {
            shipment = await client.GetShipmentByPermIdAsync(permShipmentId, cancellationToken);
        }
        catch (RomaniaRateLimitedException exception)
        {
            logger.LogWarning("Romania OneClick: {Message} Skipping until the next poll.", exception.Message);

            db.ShipmentFeedStaging.Add(new LtsShipmentFeedStagingRecord
            {
                CustomerCode = RomaniaConstants.CustomerCodeSentinel,
                CountryCode = "RO",
                EndpointKind = RomaniaOneClickEndpointKinds.DomesticShipmentLookup,
                FetchedAt = fetchedAt,
                ReferenceNo = permShipmentId,
                RawPayload = "null",
                Status = ShipmentFeedStagingStatus.Failed,
                ProcessedAt = clock.UtcNow,
                ErrorMessage = "Rate limited (HTTP 429)."
            });

            return;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "Romania OneClick: lookup failed for perm_shipment_id '{PermShipmentId}' (transfer {TransferNo}).",
                permShipmentId, transferNo);

            db.ShipmentFeedStaging.Add(new LtsShipmentFeedStagingRecord
            {
                CustomerCode = RomaniaConstants.CustomerCodeSentinel,
                CountryCode = "RO",
                EndpointKind = RomaniaOneClickEndpointKinds.DomesticShipmentLookup,
                FetchedAt = fetchedAt,
                ReferenceNo = permShipmentId,
                RawPayload = "null",
                Status = ShipmentFeedStagingStatus.Failed,
                ProcessedAt = clock.UtcNow,
                ErrorMessage = exception.Message
            });

            return;
        }

        var stagingRow = new LtsShipmentFeedStagingRecord
        {
            CustomerCode = RomaniaConstants.CustomerCodeSentinel,
            CountryCode = "RO",
            EndpointKind = RomaniaOneClickEndpointKinds.DomesticShipmentLookup,
            FetchedAt = fetchedAt,
            ReferenceNo = permShipmentId,
            RawPayload = JsonSerializer.Serialize(shipment, SerializerOptions),
            Status = ShipmentFeedStagingStatus.Pending
        };

        db.ShipmentFeedStaging.Add(stagingRow);

        if (shipment is null)
        {
            logger.LogInformation(
                "Romania OneClick: no shipment found for perm_shipment_id '{PermShipmentId}' (transfer {TransferNo}).",
                permShipmentId, transferNo);

            stagingRow.Status = ShipmentFeedStagingStatus.Processed;
            stagingRow.ProcessedAt = clock.UtcNow;
            return;
        }

        try
        {
            var changes = RomaniaMilestoneMapper.BuildMilestoneChanges(transferNo, shipment);

            if (changes.Count > 0)
            {
                var result = await milestones.ApplyAsync(
                    changes,
                    new MilestoneApplyOptions(
                        MilestoneSource.Integration,
                        EnforcePermissions: false,
                        SkipChronologyValidation: true,
                        Note: "Romania OneClick"),
                    UserPermissions.None,
                    cancellationToken);

                if (result.HasErrors)
                {
                    logger.LogWarning(
                        "Romania OneClick: {ErrorCount} milestone(s) rejected for transfer {TransferNo}: {Errors}",
                        result.Errors.Count, transferNo, string.Join("; ", result.Errors.Select(e => e.Message)));
                }
            }

            stagingRow.Status = ShipmentFeedStagingStatus.Processed;
            stagingRow.ProcessedAt = clock.UtcNow;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stagingRow.Status = ShipmentFeedStagingStatus.Failed;
            stagingRow.ProcessedAt = clock.UtcNow;
            stagingRow.ErrorMessage = exception.Message;

            logger.LogWarning(exception,
                "Romania OneClick: failed to apply milestones for transfer {TransferNo}.", transferNo);
        }
    }
}
