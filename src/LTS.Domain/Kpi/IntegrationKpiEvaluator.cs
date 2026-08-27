using LTS.Domain.Enums;

namespace LTS.Domain.Kpi;

/// <summary>
/// The four attributes an LTS_Integration KPI target is scoped by, Description text - matching how
/// LtsIntegrationShipment itself stores them, so a shipment is matched against a target by direct
/// string comparison rather than resolving a code first.
/// </summary>
public readonly record struct KpiAttributeScope(
    string? ExportType, string? LoadingPoint, string? ArrivalCustoms, string? TransportType);

/// <summary>One LTS_KpiTargets row, in the shape target resolution needs - no EF/database types.</summary>
public sealed record IntegrationKpiTargetSnapshot(
    int CountryId, IntegrationKpiStep Step, KpiAttributeScope Scope, int TargetDays, bool IsActive);

/// <summary>
/// Matches a shipment to the KPI target that applies to it. Country is required and matched
/// exactly (every country has its own KPI values); the four attributes are optional - a null
/// target column means "any" - with the most specific matching row winning, same tie-break
/// convention as the old KpiTargetResolver.
/// </summary>
public static class IntegrationKpiResolver
{
    public static int? ResolveTargetDays(
        IntegrationKpiStep step, int countryId, KpiAttributeScope shipmentScope,
        IEnumerable<IntegrationKpiTargetSnapshot> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        IntegrationKpiTargetSnapshot? best = null;
        var bestSpecificity = -1;

        foreach (var target in targets)
        {
            if (target.Step != step || target.CountryId != countryId || !target.IsActive)
            {
                continue;
            }

            if (!Matches(target.Scope, shipmentScope))
            {
                continue;
            }

            var specificity = Specificity(target.Scope);
            if (specificity > bestSpecificity)
            {
                best = target;
                bestSpecificity = specificity;
            }
        }

        return best?.TargetDays;
    }

    private static bool Matches(KpiAttributeScope target, KpiAttributeScope shipment) =>
        Matches(target.ExportType, shipment.ExportType) &&
        Matches(target.LoadingPoint, shipment.LoadingPoint) &&
        Matches(target.ArrivalCustoms, shipment.ArrivalCustoms) &&
        Matches(target.TransportType, shipment.TransportType);

    private static bool Matches(string? targetValue, string? shipmentValue) =>
        targetValue is null || string.Equals(targetValue, shipmentValue, StringComparison.OrdinalIgnoreCase);

    /// <summary>How many of the 4 optional keys a target row pins down; the highest wins a tie.</summary>
    public static int Specificity(KpiAttributeScope scope) =>
        (scope.ExportType is null ? 0 : 1) +
        (scope.LoadingPoint is null ? 0 : 1) +
        (scope.ArrivalCustoms is null ? 0 : 1) +
        (scope.TransportType is null ? 0 : 1);
}

/// <summary>One leg's start/end dates and its stored KPI deadline, ready to score.</summary>
public readonly record struct KpiLegDates(DateOnly? Start, DateOnly? End, DateOnly? Deadline);

/// <summary>
/// Scores KPI legs against their stored deadlines. Pure and side-effect free - everything it needs
/// is passed in - which keeps it fully unit-testable and independent of EF Core and the UI, the
/// same way the old KpiEvaluator was.
/// </summary>
public static class IntegrationKpiEvaluator
{
    public static bool HasRequiredAttributes(KpiAttributeScope scope) =>
        !string.IsNullOrWhiteSpace(scope.ExportType) &&
        !string.IsNullOrWhiteSpace(scope.LoadingPoint) &&
        !string.IsNullOrWhiteSpace(scope.ArrivalCustoms) &&
        !string.IsNullOrWhiteSpace(scope.TransportType);

    /// <summary>
    /// One leg's outcome: not started while its start date is empty; NoTarget once started but no
    /// deadline was ever resolved for it; OnTime/Late once its end date lands; OnTrack/Overdue
    /// against today while still running.
    /// </summary>
    public static PerformanceStatus EvaluateLeg(KpiLegDates leg, DateOnly today)
    {
        if (leg.Start is null)
        {
            return PerformanceStatus.NotStarted;
        }

        if (leg.Deadline is null)
        {
            return PerformanceStatus.NoTarget;
        }

        if (leg.End is { } end)
        {
            return end <= leg.Deadline ? PerformanceStatus.OnTime : PerformanceStatus.Late;
        }

        return today <= leg.Deadline ? PerformanceStatus.OnTrack : PerformanceStatus.Overdue;
    }

    /// <summary>
    /// A shipment's overall Performance: MissingAttributes immediately (no leg is evaluated at all)
    /// if any of the 4 required scope attributes is empty, since no leg could have been resolved
    /// against a real target in the first place. Otherwise the worst outcome across the 5
    /// shipment-scope legs plus every one of this shipment's transfers' XDock leg (XDock is scored
    /// once per transfer, since each transfer has its own actual Crossdock Departure date).
    /// </summary>
    public static PerformanceStatus EvaluateShipment(
        KpiAttributeScope scope,
        IReadOnlyList<KpiLegDates> shipmentLegs,
        IReadOnlyList<KpiLegDates> xdockLegsPerTransfer,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(shipmentLegs);
        ArgumentNullException.ThrowIfNull(xdockLegsPerTransfer);

        if (!HasRequiredAttributes(scope))
        {
            return PerformanceStatus.MissingAttributes;
        }

        var statuses = shipmentLegs
            .Concat(xdockLegsPerTransfer)
            .Select(leg => EvaluateLeg(leg, today));

        return PerformanceSeverity.Worst(statuses);
    }

    /// <summary>Which specific leg is behind a Late/Overdue result, for reporting - e.g. the delay alert mails.</summary>
    public readonly record struct DelayedLeg(IntegrationKpiStep Step, KpiLegDates Dates, PerformanceStatus Status);

    /// <summary>
    /// The first leg (in the given, chronological order) currently Late or Overdue, or null if none
    /// is. Legs are sequential in practice, so only one is ever really "active" at a time; this
    /// picks the earliest one that qualifies rather than ranking severity, since two different late
    /// legs aren't meaningfully comparable by rank the way two shipment-level rollups are.
    /// </summary>
    public static DelayedLeg? FindDelayedLeg(
        IReadOnlyList<(IntegrationKpiStep Step, KpiLegDates Dates)> legs, DateOnly today)
    {
        foreach (var (step, dates) in legs)
        {
            var status = EvaluateLeg(dates, today);
            if (status is PerformanceStatus.Late or PerformanceStatus.Overdue)
            {
                return new DelayedLeg(step, dates, status);
            }
        }

        return null;
    }
}
