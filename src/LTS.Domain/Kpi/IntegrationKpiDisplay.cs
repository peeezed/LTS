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
}
