namespace LTS.Domain.Kpi;

/// <summary>
/// Display-only helpers for showing a KPI leg's target alongside its stored deadline in a grid -
/// the pair sits between the leg's start and end milestone columns, in lifecycle order.
/// </summary>
public static class IntegrationKpiDisplay
{
    /// <summary>
    /// The number of days a KPI deadline allows for a leg, derived from its stored start date and
    /// deadline - the same value LTS_KpiTargets.TargetDays held when the deadline was computed.
    /// Null whenever either date is missing (the leg hasn't started, or nothing resolved a target).
    /// </summary>
    public static int? DaysBetween(DateOnly? start, DateOnly? deadline) =>
        start is { } s && deadline is { } d ? d.DayNumber - s.DayNumber : null;

    /// <summary>
    /// Whether a leg's own actual (end-milestone) date fell after its stored KPI deadline - the
    /// same condition IntegrationKpiEvaluator.EvaluateLeg scores as Late, surfaced here for a grid
    /// to flag the specific date that missed its target rather than only the shipment's overall
    /// Performance. False whenever either date is missing (nothing to compare yet).
    /// </summary>
    public static bool IsLate(DateOnly? actual, DateOnly? deadline) =>
        actual is { } a && deadline is { } d && a > d;
}
