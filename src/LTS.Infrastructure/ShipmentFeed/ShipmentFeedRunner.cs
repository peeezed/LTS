using System.Text.Json;
using LTS.Application.Abstractions;
using LTS.Application.ShipmentFeed;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LTS.Infrastructure.ShipmentFeed;

/// <summary>Which of the 3-4 shipment feed calls a staging row came from. Placeholder names, see ShipmentFeedClient.</summary>
internal static class ShipmentFeedEndpointKinds
{
    public const string List = "List";
    public const string Header = "Header";
    public const string Attributes = "Attributes";
    public const string Counts = "Counts";
}

/// <summary>
/// Runs one full poll: for every active country with a CustomerCode, list its shipment
/// references, fetch each one's detail calls, stage every raw response (append-only, regardless
/// of outcome), combine and standardize each shipment, then upsert it into LTS_Shipments. One
/// country's failure - or one shipment's - is caught and logged so it never stops the others.
/// </summary>
public sealed class ShipmentFeedRunner(
    IDbContextFactory<LtsIntegrationDbContext> dbFactory,
    IShipmentFeedClient client,
    IClock clock,
    ILogger<ShipmentFeedRunner> logger)
{
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
        var references = await client.FetchShipmentReferencesAsync(customerCode, cancellationToken);

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
            RawPayload = JsonSerializer.Serialize(references),
            Status = ShipmentFeedStagingStatus.Processed,
            ProcessedAt = listFetchedAt
        });

        var referenceNumbers = references
            .Select(r => r.ReferenceNo)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r!)
            .Distinct()
            .ToList();

        var results = new List<(string ReferenceNo, RawShipmentFeedDto Record, List<LtsShipmentFeedStagingRecord> StagingRows)>();

        foreach (var referenceNo in referenceNumbers)
        {
            var stagingRows = new List<LtsShipmentFeedStagingRecord>();

            var header = await FetchDetailAsync(stagingRows, customerCode, countryCode, referenceNo,
                ShipmentFeedEndpointKinds.Header, () => client.FetchShipmentHeaderAsync(referenceNo, cancellationToken));
            var attributes = await FetchDetailAsync(stagingRows, customerCode, countryCode, referenceNo,
                ShipmentFeedEndpointKinds.Attributes, () => client.FetchShipmentAttributesAsync(referenceNo, cancellationToken));
            var counts = await FetchDetailAsync(stagingRows, customerCode, countryCode, referenceNo,
                ShipmentFeedEndpointKinds.Counts, () => client.FetchShipmentCountsAsync(referenceNo, cancellationToken));

            db.ShipmentFeedStaging.AddRange(stagingRows);

            var reference = references.First(r => r.ReferenceNo == referenceNo);
            var combined = ShipmentFeedCombiner.Combine(reference, header, attributes, counts);

            results.Add((referenceNo, combined, stagingRows));
        }

        await db.SaveChangesAsync(cancellationToken);

        if (results.Count == 0)
        {
            return;
        }

        var lookups = await AttributeCodeLookupLoader.LoadAsync(db, results.Select(r => r.Record).ToList(), cancellationToken);

        foreach (var (referenceNo, record, stagingRows) in results)
        {
            try
            {
                await UpsertShipmentAsync(db, customerCode, record, lookups, cancellationToken);
                MarkPendingRows(stagingRows, ShipmentFeedStagingStatus.Processed, null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                MarkPendingRows(stagingRows, ShipmentFeedStagingStatus.Failed, exception.Message);
                logger.LogWarning(exception,
                    "Shipment feed: failed to process reference '{ReferenceNo}' ({CustomerCode}).",
                    referenceNo, customerCode);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Fetches one detail endpoint for one reference and stages its raw response immediately,
    /// regardless of what happens afterward. A call that throws is staged as Failed right away
    /// (there is no successful response to combine); a call that succeeds - including a null
    /// "no data for this endpoint" result - is staged as Pending, to be marked Processed/Failed
    /// once this shipment's overall standardize-and-upsert outcome is known.
    /// </summary>
    private async Task<T?> FetchDetailAsync<T>(
        List<LtsShipmentFeedStagingRecord> stagingRows, string customerCode, string? countryCode, string referenceNo,
        string endpointKind, Func<Task<T?>> fetch) where T : class
    {
        var fetchedAt = clock.UtcNow;

        try
        {
            var result = await fetch();

            stagingRows.Add(new LtsShipmentFeedStagingRecord
            {
                CustomerCode = customerCode,
                CountryCode = countryCode,
                EndpointKind = endpointKind,
                FetchedAt = fetchedAt,
                ReferenceNo = referenceNo,
                RawPayload = JsonSerializer.Serialize(result),
                Status = ShipmentFeedStagingStatus.Pending
            });

            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stagingRows.Add(new LtsShipmentFeedStagingRecord
            {
                CustomerCode = customerCode,
                CountryCode = countryCode,
                EndpointKind = endpointKind,
                FetchedAt = fetchedAt,
                ReferenceNo = referenceNo,
                RawPayload = "null",
                Status = ShipmentFeedStagingStatus.Failed,
                ProcessedAt = fetchedAt,
                ErrorMessage = exception.Message
            });

            logger.LogWarning(exception,
                "Shipment feed: {EndpointKind} call failed for reference '{ReferenceNo}' ({CustomerCode}).",
                endpointKind, referenceNo, customerCode);

            return null;
        }
    }

    private void MarkPendingRows(IEnumerable<LtsShipmentFeedStagingRecord> rows, ShipmentFeedStagingStatus status, string? error)
    {
        var now = clock.UtcNow;

        foreach (var row in rows.Where(r => r.Status == ShipmentFeedStagingStatus.Pending))
        {
            row.Status = status;
            row.ProcessedAt = now;
            row.ErrorMessage = error;
        }
    }

    private async Task UpsertShipmentAsync(
        LtsIntegrationDbContext db, string customerCode, RawShipmentFeedDto record,
        AttributeCodeLookups lookups, CancellationToken cancellationToken)
    {
        var fields = ShipmentStandardizer.Standardize(record, lookups);

        foreach (var warning in fields.Warnings)
        {
            logger.LogWarning("Shipment feed ({CustomerCode}, {ReferenceNo}): {Warning}",
                customerCode, fields.ReferenceNo, warning);
        }

        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.ReferenceNo == fields.ReferenceNo, cancellationToken);

        if (shipment is null)
        {
            var (currentStatus, performance) = ShipmentFeedDefaults.ForNewShipment();

            shipment = new LtsIntegrationShipment
            {
                ReferenceNo = fields.ReferenceNo,
                InvoiceNo = fields.InvoiceNo,
                CustomerCode = customerCode,
                CurrentStatus = currentStatus,
                Performance = performance
                // ArrivalCountry: intentionally left null - backfilled at read time by
                // IntegrationShipmentQueryService.BackfillArrivalCountryAsync.
            };

            db.Shipments.Add(shipment);
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
    }
}
