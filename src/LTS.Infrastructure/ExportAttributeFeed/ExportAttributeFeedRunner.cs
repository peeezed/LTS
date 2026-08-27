using LTS.Application.ExportAttributeFeed;
using LTS.Application.Tracking;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LTS.Infrastructure.ExportAttributeFeed;

/// <summary>
/// Backfills the four KPI-scoping attributes (ExportType/LoadingPoint/ArrivalCustoms/TransportType)
/// on shipments missing one of them, by calling GetLTSExportFileDetail per shipment reference
/// number, then re-scores KPI for whichever shipments actually changed via
/// IIntegrationMilestoneService.RecomputeKpiForShipmentAsync. Independent of ShipmentFeedRunner:
/// different endpoint (one shipment at a time, not a per-country bulk list), different trigger
/// (only shipments missing an attribute, not every shipment on every poll), its own poll cycle.
/// </summary>
public sealed class ExportAttributeFeedRunner(
    IDbContextFactory<LtsIntegrationDbContext> dbFactory,
    IExportAttributeFeedClient client,
    IIntegrationMilestoneService milestoneService,
    ILogger<ExportAttributeFeedRunner> logger)
{
    private const int MaxShipmentsPerRun = 200;

    /// <summary>
    /// Runs one shipment through the exact same standardize+apply+recompute path as a real poll,
    /// but fed from already-parsed data instead of a live HTTP call - for the local simulator tool
    /// (tools/ShipmentFeedSimulator) to exercise the real pipeline against LTS_Integration without
    /// needing real API access. Returns null if no shipment matches the detail's ExportFileNumber -
    /// unlike ShipmentFeedRunner, this module only ever updates an existing shipment, never creates
    /// one.
    /// </summary>
    public async Task<StandardizedExportAttributes?> SimulateAsync(
        ExportFileDetailDto detail, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var shipment = await db.Shipments
            .FirstOrDefaultAsync(s => s.ReferenceNo == detail.ExportFileNumber, cancellationToken);

        if (shipment is null)
        {
            return null;
        }

        var lookups = await ExportAttributeCodeLookupLoader.LoadAsync(db, [detail], cancellationToken);
        var fields = ExportAttributeStandardizer.Standardize(detail, lookups);

        ApplyFields(shipment, fields);
        await db.SaveChangesAsync(cancellationToken);

        await milestoneService.RecomputeKpiForShipmentAsync(shipment.ReferenceNo, cancellationToken);

        return fields;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        List<string> candidateReferenceNumbers;

        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            candidateReferenceNumbers = await db.Shipments
                .Where(s => string.IsNullOrEmpty(s.ExportType) || string.IsNullOrEmpty(s.LoadingPoint)
                    || string.IsNullOrEmpty(s.ArrivalCustoms) || string.IsNullOrEmpty(s.TransportType))
                .Select(s => s.ReferenceNo)
                .Take(MaxShipmentsPerRun)
                .ToListAsync(cancellationToken);
        }

        if (candidateReferenceNumbers.Count == 0)
        {
            return;
        }

        var fetched = new List<ExportFileDetailDto>();

        foreach (var referenceNo in candidateReferenceNumbers)
        {
            try
            {
                var detail = await client.FetchExportFileDetailAsync(referenceNo, cancellationToken);

                if (detail is not null)
                {
                    fetched.Add(detail);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception,
                    "Export attribute feed: fetch failed for '{ReferenceNo}'.", referenceNo);
            }
        }

        if (fetched.Count == 0)
        {
            return;
        }

        await using var db2 = await dbFactory.CreateDbContextAsync(cancellationToken);
        var lookups = await ExportAttributeCodeLookupLoader.LoadAsync(db2, fetched, cancellationToken);

        var updatedReferenceNumbers = new List<string>();

        foreach (var detail in fetched)
        {
            try
            {
                var shipment = await db2.Shipments
                    .FirstOrDefaultAsync(s => s.ReferenceNo == detail.ExportFileNumber, cancellationToken);

                if (shipment is null)
                {
                    continue;
                }

                var fields = ExportAttributeStandardizer.Standardize(detail, lookups);

                foreach (var warning in fields.Warnings)
                {
                    logger.LogWarning("Export attribute feed ({ReferenceNo}): {Warning}", detail.ExportFileNumber, warning);
                }

                if (ApplyFields(shipment, fields))
                {
                    updatedReferenceNumbers.Add(shipment.ReferenceNo);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception,
                    "Export attribute feed: update failed for '{ReferenceNo}'.", detail.ExportFileNumber);
            }
        }

        await db2.SaveChangesAsync(cancellationToken);

        foreach (var referenceNo in updatedReferenceNumbers)
        {
            await milestoneService.RecomputeKpiForShipmentAsync(referenceNo, cancellationToken);
        }
    }

    /// <summary>
    /// Sets only the fields the response actually resolved a non-blank value for, leaving anything
    /// already on the shipment untouched - a partial response (the source system may still be
    /// missing one of the six fields itself) must never blank out an attribute the shipment already
    /// had. Returns whether anything actually changed.
    /// </summary>
    private static bool ApplyFields(LtsIntegrationShipment shipment, StandardizedExportAttributes fields)
    {
        var changed = false;

        void Set(Func<string?> get, Action<string> set, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && get() != value)
            {
                set(value);
                changed = true;
            }
        }

        Set(() => shipment.ArrivalCustoms, v => shipment.ArrivalCustoms = v, fields.ArrivalCustoms);
        Set(() => shipment.ExportType, v => shipment.ExportType = v, fields.ExportType);
        Set(() => shipment.TransportType, v => shipment.TransportType = v, fields.TransportType);
        Set(() => shipment.LoadingPoint, v => shipment.LoadingPoint = v, fields.LoadingPoint);
        Set(() => shipment.LogisticsCompany, v => shipment.LogisticsCompany = v, fields.LogisticsCompany);
        Set(() => shipment.BrokerCompany, v => shipment.BrokerCompany = v, fields.BrokerCompany);

        return changed;
    }
}
