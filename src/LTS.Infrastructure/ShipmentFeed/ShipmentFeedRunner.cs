using System.Text.Json;
using LTS.Application.Abstractions;
using LTS.Application.ShipmentFeed;
using LTS.Domain.Enums;
using LTS.Infrastructure.Persistence;
using LTS.Infrastructure.Tracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LTS.Infrastructure.ShipmentFeed;

/// <summary>Which of the two shipment feed calls a staging row came from.</summary>
internal static class ShipmentFeedEndpointKinds
{
    public const string List = "List";
    public const string Detail = "Detail";
}

/// <summary>
/// Runs one full poll: for every active country with a CustomerCode, lists its shipments (header +
/// attribute codes in one call), fetches each one's box/store detail, stages every raw response
/// (append-only, regardless of outcome), then combines/standardizes/upserts each shipment plus its
/// transfers and boxes. One country's failure - or one shipment's - is caught and logged so it
/// never stops the others.
/// </summary>
public sealed class ShipmentFeedRunner(
    IDbContextFactory<LtsIntegrationDbContext> dbFactory,
    IShipmentFeedClient client,
    IClock clock,
    ILogger<ShipmentFeedRunner> logger)
{
    /// <summary>
    /// Runs one shipment through the exact same standardize+upsert path as a real poll, but fed
    /// from already-parsed data instead of a live HTTP call - for the local simulator tool
    /// (tools/ShipmentFeedSimulator) to exercise the real pipeline against LTS_Integration without
    /// needing real API access. Unlike RunAsync, this does not write a staging row - staging is
    /// about auditing real fetches, not manual testing - and it standardizes/upserts exactly one
    /// shipment rather than looping over every configured country.
    /// </summary>
    public async Task<StandardizedShipmentFields> SimulateAsync(
        string customerCode, InvoiceListEntryDto header, IReadOnlyList<InvoiceDetailLineDto> detailLines,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var lookups = await AttributeCodeLookupLoader.LoadAsync(db, [header], cancellationToken);
        var raw = ShipmentFeedCombiner.Combine(header, detailLines);
        var storeCache = new Dictionary<string, LtsIntegrationStore>(StringComparer.OrdinalIgnoreCase);

        await UpsertShipmentAsync(db, customerCode, raw, lookups, storeCache, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return ShipmentStandardizer.Standardize(raw, lookups);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        List<(string CustomerCode, string? CountryCode)> countries;

        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            var rows = await db.Countries.AsNoTracking()
                .Where(c => c.IsActive && c.CustomerCode != null && c.CustomerCode != "")
                .Select(c => new { c.CustomerCode, c.CountryCode })
                .ToListAsync(cancellationToken);

            countries = rows.Select(r => (r.CustomerCode!, (string?)r.CountryCode)).ToList();
        }

        foreach (var (customerCode, countryCode) in countries)
        {
            try
            {
                await RunForCountryAsync(customerCode, countryCode, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Shipment feed poll failed for customer code {CustomerCode}.", customerCode);
            }
        }
    }

    private async Task RunForCountryAsync(string customerCode, string? countryCode, CancellationToken cancellationToken)
    {
        var entries = await client.FetchInvoiceListAsync(customerCode, cancellationToken);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var listFetchedAt = clock.UtcNow;

        // The list call is staged as one row regardless of what it returned - even an empty
        // result is worth keeping in the audit trail.
        db.ShipmentFeedStaging.Add(new LtsShipmentFeedStagingRecord
        {
            CustomerCode = customerCode,
            CountryCode = countryCode,
            EndpointKind = ShipmentFeedEndpointKinds.List,
            FetchedAt = listFetchedAt,
            ReferenceNo = null,
            RawPayload = JsonSerializer.Serialize(entries),
            Status = ShipmentFeedStagingStatus.Processed,
            ProcessedAt = listFetchedAt
        });

        var validEntries = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.ExportNumber) && !string.IsNullOrWhiteSpace(e.InvoiceNumber))
            .ToList();

        var results = new List<(InvoiceListEntryDto Header, IReadOnlyList<InvoiceDetailLineDto> DetailLines, LtsShipmentFeedStagingRecord StagingRow)>();

        foreach (var entry in validEntries)
        {
            var (detailLines, stagingRow) = await FetchDetailAsync(customerCode, countryCode, entry, cancellationToken);
            db.ShipmentFeedStaging.Add(stagingRow);
            results.Add((entry, detailLines, stagingRow));
        }

        await db.SaveChangesAsync(cancellationToken);

        if (results.Count == 0)
        {
            return;
        }

        var lookups = await AttributeCodeLookupLoader.LoadAsync(db, validEntries, cancellationToken);

        // Shared across every shipment in this poll, keyed by CurrAccCode: two different
        // shipments in the same run landing at the same still-unmapped store must share one
        // EnsureStoreAsync insert rather than each querying the DB (which won't see the other's
        // not-yet-saved Add) and racing duplicate rows into this one unsaved DbContext.
        var storeCache = new Dictionary<string, LtsIntegrationStore>(StringComparer.OrdinalIgnoreCase);

        foreach (var (header, detailLines, stagingRow) in results)
        {
            try
            {
                var raw = ShipmentFeedCombiner.Combine(header, detailLines);
                await UpsertShipmentAsync(db, customerCode, raw, lookups, storeCache, cancellationToken);

                // A detail call that itself failed already left this row Failed, with the real
                // reason recorded - the header (attribute codes, invoice fields) still landed via
                // the branch above, just with zero transfers this tick, so that Failed status
                // and its ErrorMessage are left alone rather than being overwritten to Processed.
                if (stagingRow.Status == ShipmentFeedStagingStatus.Pending)
                {
                    stagingRow.Status = ShipmentFeedStagingStatus.Processed;
                    stagingRow.ProcessedAt = clock.UtcNow;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                stagingRow.Status = ShipmentFeedStagingStatus.Failed;
                stagingRow.ProcessedAt = clock.UtcNow;
                stagingRow.ErrorMessage = exception.Message;
                logger.LogWarning(exception,
                    "Shipment feed: failed to process reference '{ReferenceNo}' ({CustomerCode}).",
                    header.ExportNumber, customerCode);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Fetches one shipment's detail lines and stages the raw response immediately, regardless of
    /// what happens afterward. A call that throws is staged as Failed right away, with an empty
    /// line list - the shipment's header still gets processed from the list entry alone (see
    /// RunForCountryAsync), just with zero transfers until a later poll succeeds.
    /// </summary>
    private async Task<(IReadOnlyList<InvoiceDetailLineDto> DetailLines, LtsShipmentFeedStagingRecord StagingRow)> FetchDetailAsync(
        string customerCode, string? countryCode, InvoiceListEntryDto entry, CancellationToken cancellationToken)
    {
        var fetchedAt = clock.UtcNow;

        try
        {
            var detailLines = await client.FetchInvoiceDetailAsync(entry.InvoiceNumber, cancellationToken);

            return (detailLines, new LtsShipmentFeedStagingRecord
            {
                CustomerCode = customerCode,
                CountryCode = countryCode,
                EndpointKind = ShipmentFeedEndpointKinds.Detail,
                FetchedAt = fetchedAt,
                ReferenceNo = entry.ExportNumber,
                RawPayload = JsonSerializer.Serialize(detailLines),
                Status = ShipmentFeedStagingStatus.Pending
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "Shipment feed: detail call failed for invoice '{InvoiceNumber}' ({CustomerCode}).",
                entry.InvoiceNumber, customerCode);

            return ([], new LtsShipmentFeedStagingRecord
            {
                CustomerCode = customerCode,
                CountryCode = countryCode,
                EndpointKind = ShipmentFeedEndpointKinds.Detail,
                FetchedAt = fetchedAt,
                ReferenceNo = entry.ExportNumber,
                RawPayload = "[]",
                Status = ShipmentFeedStagingStatus.Failed,
                ProcessedAt = fetchedAt,
                ErrorMessage = exception.Message
            });
        }
    }

    private async Task UpsertShipmentAsync(
        LtsIntegrationDbContext db, string customerCode, RawShipmentFeedDto record,
        AttributeCodeLookups lookups, Dictionary<string, LtsIntegrationStore> storeCache,
        CancellationToken cancellationToken)
    {
        var fields = ShipmentStandardizer.Standardize(record, lookups);

        foreach (var warning in fields.Warnings)
        {
            logger.LogWarning("Shipment feed ({CustomerCode}, {ReferenceNo}): {Warning}",
                customerCode, fields.ReferenceNo, warning);
        }

        // Resolved once per shipment (not just for a new one) - EnsureStoreAsync needs it for
        // every transfer below, new shipment or not, since a store can arrive unmapped on any poll.
        var country = await db.Countries.AsNoTracking()
            .Where(c => c.CustomerCode == customerCode)
            .Select(c => new { c.Id, c.CountryDescription })
            .FirstOrDefaultAsync(cancellationToken);

        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.ReferenceNo == fields.ReferenceNo, cancellationToken);
        TrackingStatus seedStatus;

        if (shipment is null)
        {
            var (status, currentStatus, performance) = ShipmentFeedDefaults.ForNewShipment();
            seedStatus = status;

            shipment = new LtsIntegrationShipment
            {
                ReferenceNo = fields.ReferenceNo,
                InvoiceNo = fields.InvoiceNo,
                CustomerCode = customerCode,
                CurrentStatus = currentStatus,
                Performance = performance,
                // CustomerCode already tells us the country (it's how a shipment is matched to one
                // at all - see IntegrationShipmentQueryService), so there's no need to wait for
                // that service's read-time ArrivalCountry backfill on a shipment created here. That
                // backfill still runs harmlessly for anything that lands without it some other way.
                ArrivalCountry = country?.CountryDescription
            };

            db.Shipments.Add(shipment);

            // Created alongside the shipment, not lazily on its first milestone write: KPI
            // deadline computation (IntegrationKpiCalculator) and any future automated system
            // writing dates need a row to read/update from the moment the shipment exists, not
            // just from the moment someone first types a date into it.
            db.ShipmentDates.Add(new LtsIntegrationShipmentDate { ReferenceNo = fields.ReferenceNo });
        }
        else
        {
            // Never read CurrentStatus back as a milestone floor - it may already hold an
            // aggregated (transfer-driven) value. Derive fresh from LTS_ShipmentDates instead,
            // the same rule ShipmentStatusAggregator.MilestoneStatus documents and enforces
            // everywhere else a seed status is needed.
            var shipmentDate = await db.ShipmentDates.AsNoTracking()
                .FirstOrDefaultAsync(d => d.ReferenceNo == fields.ReferenceNo, cancellationToken);
            seedStatus = ShipmentStatusAggregator.MilestoneStatus(shipmentDate);
        }

        // CurrentStatus/Performance are NOT touched here on an existing row: a later module
        // advances them from real milestone dates, so overwriting them on every re-fetch of this
        // authoritative-but-unrelated header data would silently regress status back to
        // "Created"/"Not Started" forever.
        shipment.InvoiceNo = fields.InvoiceNo;
        shipment.InvoiceDate = fields.InvoiceDate;
        shipment.CustomerCode = customerCode;
        shipment.ArrivalCustoms = fields.ArrivalCustoms;
        shipment.ExportType = fields.ExportType;
        shipment.TransportType = fields.TransportType;
        shipment.LoadingPoint = fields.LoadingPoint;
        shipment.LogisticsCompany = fields.LogisticsCompany;
        shipment.BrokerCompany = fields.BrokerCompany;
        shipment.TotalTransfers = fields.TotalTransfers;
        shipment.TotalBoxes = fields.TotalBoxes;
        shipment.TotalItems = fields.TotalItems;

        foreach (var transferFields in fields.Transfers)
        {
            await UpsertTransferAsync(db, fields.ReferenceNo, transferFields, seedStatus, country?.Id, storeCache, cancellationToken);
        }
    }

    private static async Task UpsertTransferAsync(
        LtsIntegrationDbContext db, string referenceNo, StandardizedTransferFields fields,
        TrackingStatus seedStatus, int? countryId, Dictionary<string, LtsIntegrationStore> storeCache,
        CancellationToken cancellationToken)
    {
        var transfer = await db.ShipmentTransfers.FirstOrDefaultAsync(
            t => t.ReferenceNo == referenceNo && t.TransferNo == fields.TransferNo, cancellationToken);

        if (transfer is null)
        {
            var (currentStatus, performance) = ShipmentFeedDefaults.ForNewTransfer(seedStatus);

            transfer = new LtsIntegrationShipmentTransfer
            {
                ReferenceNo = referenceNo,
                TransferNo = fields.TransferNo,
                CurrentStatus = currentStatus,
                Performance = performance
            };

            db.ShipmentTransfers.Add(transfer);

            // Same reasoning as the shipment's own date row above: created eagerly so a transfer
            // that has never had a milestone entered still has somewhere for a KPI deadline (or a
            // future automated write) to land, instead of only appearing on its first date write.
            db.ShipmentTransferDates.Add(new LtsIntegrationShipmentTransferDate { TransferNo = fields.TransferNo });
        }

        // Same insert-only rule as the shipment: CurrentStatus/Performance are never touched on
        // a re-fetch here, so real milestone dates entered since are never regressed.
        // ReceivingStoreCode is misleadingly named against our own Store model: the feed's own
        // "StoreCode" field actually carries the store's CurrAccCode (its accounting/ERP code),
        // not the StoreCode Master Data assigns - see LtsIntegrationStore's own doc comment.
        transfer.ReceivingStoreCode = fields.ReceivingStoreCode;
        transfer.TotalBoxes = fields.TotalBoxes;
        transfer.TotalItems = fields.TotalItems;

        if (countryId is { } id && !string.IsNullOrWhiteSpace(fields.ReceivingStoreCode))
        {
            await EnsureStoreAsync(db, id, fields.ReceivingStoreCode, storeCache, cancellationToken);
        }

        foreach (var boxFields in fields.Boxes)
        {
            var box = await db.Boxes.FirstOrDefaultAsync(
                b => b.TransferNo == fields.TransferNo && b.PackageNo == boxFields.PackageNo, cancellationToken);

            if (box is null)
            {
                // Only that the box exists is established here - PreAcceptanceDate/AcceptanceDate
                // are a later module's concern (real store-scan data), same as transfer milestone
                // dates. LTS_Boxes.Status isn't read by any tracking logic (ShipmentStatusAggregator
                // only reads the two date columns off a box), so this default is cosmetic only.
                db.Boxes.Add(new LtsIntegrationBox
                {
                    TransferNo = fields.TransferNo,
                    PackageNo = boxFields.PackageNo,
                    Status = TrackingStatus.Created.ToDisplay()
                });
            }
        }
    }

    /// <summary>
    /// Failsafe for a store the feed references that LTS_Stores doesn't have a row for yet:
    /// creates one with just the CurrAccCode, leaving StoreCode/StoreDescription/City null for an
    /// admin to fill in via Master Data - rather than letting an unrecognised store block the
    /// transfer from being created. <paramref name="cache"/> is shared across the whole poll run
    /// (see its call sites), since a plain DB query wouldn't see another not-yet-saved insert for
    /// the same store made earlier in the same run.
    /// </summary>
    private static async Task EnsureStoreAsync(
        LtsIntegrationDbContext db, int countryId, string currAccCode,
        Dictionary<string, LtsIntegrationStore> cache, CancellationToken cancellationToken)
    {
        if (cache.ContainsKey(currAccCode))
        {
            return;
        }

        var store = await db.Stores.FirstOrDefaultAsync(
            s => s.CountryId == countryId && s.CurrAccCode == currAccCode, cancellationToken);

        if (store is null)
        {
            store = new LtsIntegrationStore { CountryId = countryId, CurrAccCode = currAccCode };
            db.Stores.Add(store);
        }

        cache[currAccCode] = store;
    }
}
