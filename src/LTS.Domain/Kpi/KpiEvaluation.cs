using LTS.Domain.Enums;

namespace LTS.Domain.Kpi;

/// <summary>Score for a single step of one shipment or transfer.</summary>
/// <param name="Step">Which interval was measured.</param>
/// <param name="StartDate">Date of the opening milestone, if reached.</param>
/// <param name="EndDate">Date of the closing milestone, if reached.</param>
/// <param name="TargetDays">Matched KPI target, or <c>null</c> when no target covers this shipment.</param>
/// <param name="ActualDays">
/// Days taken when finished, or days elapsed so far when still running. <c>null</c> before the step starts.
/// </param>
/// <param name="Status">Outcome of comparing actual against target.</param>
public sealed record StepEvaluation(
    KpiStep Step,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int? TargetDays,
    int? ActualDays,
    PerformanceStatus Status)
{
    /// <summary>Days over (positive) or under (negative) target. <c>null</c> when unscored.</summary>
    public int? VarianceDays => TargetDays is null || ActualDays is null ? null : ActualDays - TargetDays;

    public bool IsFinished => EndDate is not null;

    public bool IsRunning => StartDate is not null && EndDate is null;
}

/// <summary>The full KPI picture for one shipment or transfer.</summary>
/// <param name="Overall">Worst status across all scored steps — the grid's Performance column.</param>
/// <param name="Steps">Per-step detail, in lifecycle order.</param>
public sealed record KpiEvaluation(PerformanceStatus Overall, IReadOnlyList<StepEvaluation> Steps)
{
    public static readonly KpiEvaluation Empty = new(PerformanceStatus.NotStarted, []);

    public StepEvaluation? this[KpiStep step] => Steps.FirstOrDefault(s => s.Step == step);

    /// <summary>
    /// The leg currently running, if any — what the shipment is waiting on right now. End-to-end
    /// totals are skipped: they run for the whole journey and would always shadow the real answer.
    /// </summary>
    public StepEvaluation? CurrentStep =>
        Steps.LastOrDefault(s => s.IsRunning && !KpiStepCatalog.Get(s.Step).IsTotal);

    public IEnumerable<StepEvaluation> BreachedSteps =>
        Steps.Where(s => s.Status is PerformanceStatus.Late or PerformanceStatus.Overdue);
}
