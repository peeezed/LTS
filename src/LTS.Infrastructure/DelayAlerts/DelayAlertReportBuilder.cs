using LTS.Application.DelayAlerts;
using LTS.Domain.Enums;
using LTS.Domain.Kpi;
using LTS.Infrastructure.Persistence;
using LTS.Infrastructure.Tracking;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.DelayAlerts;

/// <summary>
/// Builds the two delay alert reports fresh from raw dates + active KPI targets at send time -
/// never from the stored Shipment.Performance/Transfer.Performance columns, since those can go
/// stale purely from time passing (nothing periodically re-evaluates a running leg into Overdue on
/// its own) and, for the shipment mail specifically, Shipment.Performance also folds in the Xdock
/// leg, which does not belong to "delayed until Crossdock Arrival" scope. countryId here is the
/// raw LTS_Countries.ID, matching how DelayAlertRunner and LTS_DelayAlertConfigs both work in raw
/// ids throughout.
/// </summary>
internal static class DelayAlertReportBuilder
{
    private const int TailDays = 7;

    public static async Task<IReadOnlyList<ShipmentDelayAlertRow>> BuildShipmentRowsAsync(
        LtsIntegrationDbContext db, int countryId, DateOnly today, CancellationToken cancellationToken)
    {
        var customerCode = await CustomerCodeForAsync(db, countryId, cancellationToken);
        if (customerCode is null)
        {
            return [];
        }

        var cutoff = today.AddDays(-TailDays);

        var candidates = await (
            from s in db.Shipments.AsNoTracking()
            join d in db.ShipmentDates.AsNoTracking() on s.ReferenceNo equals d.ReferenceNo
            where s.CustomerCode == customerCode
                && (d.CrossdockArrivalDate == null || d.CrossdockArrivalDate >= cutoff)
            select new { Shipment = s, Date = d }
        ).ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return [];
        }

        var snapshots = IntegrationKpiCalculator.ToSnapshots(
            await db.KpiTargets.AsNoTracking().Where(t => t.CountryId == countryId && t.IsActive).ToListAsync(cancellationToken));

        var rows = new List<ShipmentDelayAlertRow>();

        foreach (var candidate in candidates)
        {
            var shipment = candidate.Shipment;
            var date = candidate.Date;
            var scope = IntegrationKpiCalculator.ScopeOf(shipment);

            if (!IntegrationKpiEvaluator.HasRequiredAttributes(scope))
            {
                continue; // MissingAttributes shipments are a separate, not-yet-built report - see docs/codebase-audit.md
            }

            var legs = IntegrationKpiCatalog.All
                .Where(def => def.Step is not (IntegrationKpiStep.Xdock or IntegrationKpiStep.LocalTransportation))
                .Select(def => (def.Step, Dates: new KpiLegDates(
                    ShipmentStatusAggregator.GetDate(date, def.From),
                    ShipmentStatusAggregator.GetDate(date, def.To),
                    IntegrationKpiCalculator.GetDeadline(date, def.Step))))
                .ToList();

            var overall = IntegrationKpiEvaluator.EvaluateShipment(
                scope, legs.Select(l => l.Dates).ToList(), [], today);

            if (overall is not (PerformanceStatus.Late or PerformanceStatus.Overdue))
            {
                continue;
            }

            var delayed = IntegrationKpiEvaluator.FindDelayedLeg(legs, today);
            if (delayed is not { } d)
            {
                continue; // defensive - overall was Late/Overdue so a leg should always be found
            }

            var delayEndDate = d.Status == PerformanceStatus.Late ? d.Dates.End : null;
            var delayStartDate = d.Dates.Deadline!.Value;

            rows.Add(new ShipmentDelayAlertRow(
                InvoiceNo: shipment.InvoiceNo,
                ReferenceNo: shipment.ReferenceNo,
                ExportType: shipment.ExportType,
                LoadingPoint: shipment.LoadingPoint,
                ArrivalCustoms: shipment.ArrivalCustoms,
                TransportType: shipment.TransportType,
                LogisticsCompany: shipment.LogisticsCompany,
                BrokerCompany: shipment.BrokerCompany,
                CurrentStatus: shipment.CurrentStatus,
                DelayPhase: IntegrationKpiCatalog.Get(d.Step).DisplayName,
                DelayedDays: (delayEndDate ?? today).DayNumber - delayStartDate.DayNumber,
                DelayStartDate: delayStartDate,
                DelayEndDate: delayEndDate));
        }

        return rows;
    }

    public static async Task<IReadOnlyList<TransferDelayAlertRow>> BuildTransferRowsAsync(
        LtsIntegrationDbContext db, int countryId, DateOnly today, CancellationToken cancellationToken)
    {
        var customerCode = await CustomerCodeForAsync(db, countryId, cancellationToken);
        if (customerCode is null)
        {
            return [];
        }

        var cutoff = today.AddDays(-TailDays);

        var candidates = await (
            from t in db.ShipmentTransfers.AsNoTracking()
            join s in db.Shipments.AsNoTracking() on t.ReferenceNo equals s.ReferenceNo
            join sd in db.ShipmentDates.AsNoTracking() on s.ReferenceNo equals sd.ReferenceNo into sdates
            from sd in sdates.DefaultIfEmpty()
            join td in db.ShipmentTransferDates.AsNoTracking() on t.TransferNo equals td.TransferNo into tdates
            from td in tdates.DefaultIfEmpty()
            where s.CustomerCode == customerCode
                && (td == null || td.StoreArrivalDate == null || td.StoreArrivalDate >= cutoff)
            select new { Transfer = t, Shipment = s, ShipmentDate = sd, TransferDate = td }
        ).ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return [];
        }

        var rows = new List<TransferDelayAlertRow>();

        foreach (var candidate in candidates)
        {
            var shipment = candidate.Shipment;
            var transfer = candidate.Transfer;
            var transferDate = candidate.TransferDate;
            var scope = IntegrationKpiCalculator.ScopeOf(shipment);

            if (!IntegrationKpiEvaluator.HasRequiredAttributes(scope) || transferDate is null)
            {
                continue;
            }

            var legs = new List<(IntegrationKpiStep Step, KpiLegDates Dates)>
            {
                (IntegrationKpiStep.Xdock, new KpiLegDates(
                    candidate.ShipmentDate?.CrossdockArrivalDate, transferDate.CrossdockDepartureDate, transferDate.KPICrossdockDepartureDate)),
                (IntegrationKpiStep.LocalTransportation, new KpiLegDates(
                    transferDate.CrossdockDepartureDate, transferDate.StoreArrivalDate, transferDate.KPILocalTransportation))
            };

            var overall = IntegrationKpiEvaluator.EvaluateShipment(scope, [], legs.Select(l => l.Dates).ToList(), today);

            if (overall is not (PerformanceStatus.Late or PerformanceStatus.Overdue))
            {
                continue;
            }

            var delayed = IntegrationKpiEvaluator.FindDelayedLeg(legs, today);
            if (delayed is not { } d)
            {
                continue;
            }

            var delayEndDate = d.Status == PerformanceStatus.Late ? d.Dates.End : null;
            var delayStartDate = d.Dates.Deadline!.Value;

            rows.Add(new TransferDelayAlertRow(
                InvoiceNo: shipment.InvoiceNo,
                ReferenceNo: shipment.ReferenceNo,
                TransferNo: transfer.TransferNo,
                ReceivingStore: transfer.ReceivingStoreCode,
                ExportType: shipment.ExportType,
                LoadingPoint: shipment.LoadingPoint,
                ArrivalCustoms: shipment.ArrivalCustoms,
                TransportType: shipment.TransportType,
                LogisticsCompany: shipment.LogisticsCompany,
                BrokerCompany: shipment.BrokerCompany,
                CurrentStatus: transfer.CurrentStatus,
                DelayPhase: IntegrationKpiCatalog.Get(d.Step).DisplayName,
                DelayedDays: (delayEndDate ?? today).DayNumber - delayStartDate.DayNumber,
                DelayStartDate: delayStartDate,
                DelayEndDate: delayEndDate));
        }

        return rows;
    }

    private static async Task<string?> CustomerCodeForAsync(
        LtsIntegrationDbContext db, int countryId, CancellationToken cancellationToken)
    {
        var customerCode = await db.Countries.AsNoTracking()
            .Where(c => c.Id == countryId)
            .Select(c => c.CustomerCode)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(customerCode) ? null : customerCode;
    }
}
