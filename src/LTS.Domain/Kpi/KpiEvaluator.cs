using LTS.Domain.Enums;

namespace LTS.Domain.Kpi;

/// <summary>
/// Scores milestone dates against KPI targets. Pure and side-effect free: everything it needs
/// — the dates, the matched targets, today's date and the day-counting rule — is passed in,
/// which keeps it fully unit-testable and independent of EF Core and the UI.
/// </summary>
public sealed class KpiEvaluator(IDayCounter? dayCounter = null)
{
    /// <summary>A running step is flagged AtRisk once it has consumed this fraction of its target.</summary>
    public const double AtRiskThreshold = 0.8;

    private readonly IDayCounter _dayCounter = dayCounter ?? CalendarDayCounter.Instance;

    /// <summary>
    /// Scores the given steps.
    /// </summary>
    /// <param name="dates">
    /// Milestone dates available for the entity. For a transfer this must include the parent
    /// shipment's dates too, because steps such as Crossdock Arrival → Crossdock Departure
    /// straddle the two.
    /// </param>
    /// <param name="targetDays">Resolves the KPI target for a step, or <c>null</c> when none matches.</param>
    /// <param name="today">The date "now" is measured from, for steps that are still running.</param>
    /// <param name="steps">Steps to score; defaults to every step in the catalog.</param>
    public KpiEvaluation Evaluate(
        IReadOnlyDictionary<MilestoneType, DateOnly?> dates,
        Func<KpiStep, int?> targetDays,
        DateOnly today,
        IEnumerable<KpiStepDefinition>? steps = null)
    {
        ArgumentNullException.ThrowIfNull(dates);
        ArgumentNullException.ThrowIfNull(targetDays);

        var evaluations = new List<StepEvaluation>();

        foreach (var definition in steps ?? KpiStepCatalog.All)
        {
            evaluations.Add(EvaluateStep(definition, dates, targetDays(definition.Step), today));
        }

        var overall = PerformanceSeverity.Worst(evaluations.Select(e => e.Status));
        return new KpiEvaluation(overall, evaluations);
    }

    private StepEvaluation EvaluateStep(
        KpiStepDefinition definition,
        IReadOnlyDictionary<MilestoneType, DateOnly?> dates,
        int? target,
        DateOnly today)
    {
        var start = Lookup(dates, definition.From);
        var end = Lookup(dates, definition.To);

        // The clock has not started, so there is nothing to score yet.
        if (start is null)
        {
            return new StepEvaluation(definition.Step, null, end, target, null, PerformanceStatus.NotStarted);
        }

        var finished = end is not null;
        var elapsed = _dayCounter.DaysBetween(start.Value, finished ? end!.Value : today);

        // A step that started in the future (data entry error, or a date not yet reached) is
        // clamped to zero rather than reported as negative progress.
        if (elapsed < 0)
        {
            elapsed = 0;
        }

        if (target is null)
        {
            return new StepEvaluation(definition.Step, start, end, null, elapsed, PerformanceStatus.NoTarget);
        }

        var status = finished
            ? elapsed <= target.Value ? PerformanceStatus.OnTime : PerformanceStatus.Late
            : RunningStatus(elapsed, target.Value);

        return new StepEvaluation(definition.Step, start, end, target, elapsed, status);
    }

    private static PerformanceStatus RunningStatus(int elapsed, int target)
    {
        if (elapsed > target)
        {
            return PerformanceStatus.Overdue;
        }

        // A zero-day target is met only on the same day, so anything still open is already at risk.
        return target == 0 || elapsed >= target * AtRiskThreshold
            ? PerformanceStatus.AtRisk
            : PerformanceStatus.OnTrack;
    }

    private static DateOnly? Lookup(IReadOnlyDictionary<MilestoneType, DateOnly?> dates, MilestoneType type) =>
        dates.TryGetValue(type, out var date) ? date : null;
}
