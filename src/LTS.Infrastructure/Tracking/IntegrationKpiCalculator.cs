using LTS.Domain.Enums;
using LTS.Domain.Kpi;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Tracking;

/// <summary>
/// Computes and persists the five shipment-scope KPI*Date deadline columns plus every touched
/// transfer's KPICrossdockDepartureDate (the one KPI leg - XDock - whose start is on the shipment
/// but whose end is on the transfer), then derives a shipment's overall Performance from them. The
/// EF-touching adapter over the pure IntegrationKpiEvaluator/IntegrationKpiResolver in LTS.Domain.Kpi
/// - the actual resolution/scoring logic lives there, fully unit-testable without a database.
/// </summary>
internal static class IntegrationKpiCalculator
{
    public static KpiAttributeScope ScopeOf(LtsIntegrationShipment shipment) => new(
        shipment.ExportType, shipment.LoadingPoint, shipment.ArrivalCustoms, shipment.TransportType);

    /// <summary>
    /// Recomputes and stores every KPI*Date deadline this shipment's dates currently support, and
    /// returns the transfer-date rows it touched along the way (creating one for a transfer that
    /// didn't have one yet) - the caller reuses that list for EvaluatePerformance rather than
    /// re-querying, since a row just added here isn't visible to a fresh query against the same
    /// unsaved DbContext. Does nothing at all, and returns an empty list, if the shipment is
    /// missing any of its 4 required scope attributes - a deadline computed against an incomplete
    /// scope would not reflect a real target.
    /// </summary>
    public static async Task<IReadOnlyList<LtsIntegrationShipmentTransferDate>> RecomputeDeadlinesAsync(
        LtsIntegrationDbContext db,
        LtsIntegrationShipment shipment,
        LtsIntegrationShipmentDate date,
        int countryId,
        IReadOnlyList<LtsIntegrationKpiTarget> targets,
        CancellationToken cancellationToken)
    {
        var scope = ScopeOf(shipment);

        if (!IntegrationKpiEvaluator.HasRequiredAttributes(scope))
        {
            return [];
        }

        var snapshots = ToSnapshots(targets);

        foreach (var definition in IntegrationKpiCatalog.All)
        {
            if (definition.Step is IntegrationKpiStep.Xdock or IntegrationKpiStep.LocalTransportation)
            {
                continue; // both handled separately below - their dates live on the transfer, not here
            }

            var start = ShipmentStatusAggregator.GetDate(date, definition.From);
            if (start is null)
            {
                continue; // leg hasn't started; leave its deadline column as-is (still null)
            }

            var targetDays = IntegrationKpiResolver.ResolveTargetDays(definition.Step, countryId, scope, snapshots);
            SetDeadline(date, definition.Step, targetDays is { } days ? start.Value.AddDays(days) : null);
        }

        if (date.CrossdockArrivalDate is not { } crossdockArrival)
        {
            return [];
        }

        var xdockTargetDays = IntegrationKpiResolver.ResolveTargetDays(
            IntegrationKpiStep.Xdock, countryId, scope, snapshots);
        var xdockDeadline = xdockTargetDays is { } xd ? crossdockArrival.AddDays(xd) : (DateOnly?)null;

        var localTransportTargetDays = IntegrationKpiResolver.ResolveTargetDays(
            IntegrationKpiStep.LocalTransportation, countryId, scope, snapshots);

        var transferNumbers = await db.ShipmentTransfers
            .Where(t => t.ReferenceNo == shipment.ReferenceNo)
            .Select(t => t.TransferNo)
            .ToListAsync(cancellationToken);

        var transferDates = new List<LtsIntegrationShipmentTransferDate>();

        foreach (var transferNo in transferNumbers)
        {
            var transferDate = await db.ShipmentTransferDates
                .FirstOrDefaultAsync(d => d.TransferNo == transferNo, cancellationToken);

            if (transferDate is null)
            {
                transferDate = new LtsIntegrationShipmentTransferDate { TransferNo = transferNo };
                db.ShipmentTransferDates.Add(transferDate);
            }

            transferDate.KPICrossdockDepartureDate = xdockDeadline;

            // Fully transfer-scope, unlike Xdock: only computed once this transfer's own Crossdock
            // Departure has actually happened.
            transferDate.KPILocalTransportation = transferDate.CrossdockDepartureDate is { } departed && localTransportTargetDays is { } ld
                ? departed.AddDays(ld)
                : null;

            transferDates.Add(transferDate);
        }

        return transferDates;
    }

    /// <summary>
    /// A shipment's overall Performance, from its own dates/deadlines plus every one of its
    /// transfers' XDock leg. Reads only - RecomputeDeadlinesAsync must have already run (in the
    /// same call, for the same shipment) for the deadlines this reads to be current.
    /// </summary>
    public static PerformanceStatus EvaluatePerformance(
        LtsIntegrationShipment shipment,
        LtsIntegrationShipmentDate? date,
        IReadOnlyList<LtsIntegrationShipmentTransferDate> transferDates,
        DateOnly today)
    {
        var scope = ScopeOf(shipment);

        if (date is null)
        {
            return IntegrationKpiEvaluator.HasRequiredAttributes(scope)
                ? PerformanceStatus.NotStarted
                : PerformanceStatus.MissingAttributes;
        }

        var shipmentLegs = IntegrationKpiCatalog.All
            .Where(d => d.Step is not (IntegrationKpiStep.Xdock or IntegrationKpiStep.LocalTransportation))
            .Select(d => new KpiLegDates(
                ShipmentStatusAggregator.GetDate(date, d.From),
                ShipmentStatusAggregator.GetDate(date, d.To),
                GetDeadline(date, d.Step)))
            .ToList();

        var xdockLegs = transferDates
            .Select(t => new KpiLegDates(date.CrossdockArrivalDate, t.CrossdockDepartureDate, t.KPICrossdockDepartureDate))
            .ToList();

        return IntegrationKpiEvaluator.EvaluateShipment(scope, shipmentLegs, xdockLegs, today);
    }

    /// <summary>
    /// One transfer's own Performance - worst of {Xdock, Local Transportation} - computed and
    /// persisted independently of the shipment's own Performance (which still folds Xdock into
    /// itself too; that's unrelated to this value and left as-is). Reuses EvaluateShipment's
    /// worst-of-legs logic with an empty shipment-legs list, exactly mirroring how the shipment
    /// mail's own evaluation reuses the same method the other way around (empty xdock-legs list).
    /// </summary>
    public static PerformanceStatus EvaluateTransferPerformance(
        KpiAttributeScope scope,
        DateOnly? shipmentCrossdockArrival,
        LtsIntegrationShipmentTransferDate? transferDate,
        DateOnly today)
    {
        if (transferDate is null)
        {
            return IntegrationKpiEvaluator.HasRequiredAttributes(scope)
                ? PerformanceStatus.NotStarted
                : PerformanceStatus.MissingAttributes;
        }

        var xdockLeg = new KpiLegDates(shipmentCrossdockArrival, transferDate.CrossdockDepartureDate, transferDate.KPICrossdockDepartureDate);
        var localTransportLeg = new KpiLegDates(transferDate.CrossdockDepartureDate, transferDate.StoreArrivalDate, transferDate.KPILocalTransportation);

        return IntegrationKpiEvaluator.EvaluateShipment(scope, [], [xdockLeg, localTransportLeg], today);
    }

    /// <summary>Internal (not private) so DelayAlertReportBuilder can reuse it - both classes are Infrastructure-internal.</summary>
    internal static IReadOnlyList<IntegrationKpiTargetSnapshot> ToSnapshots(IReadOnlyList<LtsIntegrationKpiTarget> targets) =>
        [.. targets.Select(t => new IntegrationKpiTargetSnapshot(
            t.CountryId, t.Step,
            new KpiAttributeScope(t.ExportType, t.LoadingPoint, t.ArrivalCustoms, t.TransportType),
            t.TargetDays, t.IsActive))];

    /// <summary>Internal (not private) so DelayAlertReportBuilder can reuse it for the shipment mail's own leg evaluation.</summary>
    internal static DateOnly? GetDeadline(LtsIntegrationShipmentDate date, IntegrationKpiStep step) => step switch
    {
        IntegrationKpiStep.LoadingToCustomsClearance => date.KPICustomsClearanceDate,
        IntegrationKpiStep.CustomsToDeparture => date.KPIDepartureDate,
        IntegrationKpiStep.InternationalTransportation => date.KPIArrivalToDestinationDate,
        IntegrationKpiStep.CountryCustomsClearance => date.KPIArrivalCustomsEndDate,
        IntegrationKpiStep.LeadTimeToXdock => date.KPILeadTimeToXdock,
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Not a shipment-scope KPI leg.")
    };

    private static void SetDeadline(LtsIntegrationShipmentDate date, IntegrationKpiStep step, DateOnly? value)
    {
        switch (step)
        {
            case IntegrationKpiStep.LoadingToCustomsClearance: date.KPICustomsClearanceDate = value; break;
            case IntegrationKpiStep.CustomsToDeparture: date.KPIDepartureDate = value; break;
            case IntegrationKpiStep.InternationalTransportation: date.KPIArrivalToDestinationDate = value; break;
            case IntegrationKpiStep.CountryCustomsClearance: date.KPIArrivalCustomsEndDate = value; break;
            case IntegrationKpiStep.LeadTimeToXdock: date.KPILeadTimeToXdock = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(step), step, "Not a shipment-scope KPI leg.");
        }
    }
}
