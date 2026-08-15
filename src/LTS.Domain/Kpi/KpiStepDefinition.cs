using LTS.Domain.Enums;

namespace LTS.Domain.Kpi;

/// <summary>
/// Defines a measured interval: which two milestones bound it and where it is scored.
/// </summary>
/// <param name="Step">The step being defined.</param>
/// <param name="From">Milestone that starts the clock.</param>
/// <param name="To">Milestone that stops it.</param>
/// <param name="Scope">
/// Where the step is evaluated. Steps that cross the crossdock boundary are scored on the
/// transfer, because the shipment's remaining life happens per store.
/// </param>
/// <param name="DisplayName">Label used in the KPI admin grid and Excel template.</param>
/// <param name="IsTotal">True for end-to-end totals rather than a single leg.</param>
public sealed record KpiStepDefinition(
    KpiStep Step,
    MilestoneType From,
    MilestoneType To,
    MilestoneScope Scope,
    string DisplayName,
    bool IsTotal = false);
