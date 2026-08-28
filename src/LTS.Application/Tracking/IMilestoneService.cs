using LTS.Application.Security;
using LTS.Domain.Kpi;

namespace LTS.Application.Tracking;

/// <summary>
/// The one way milestone dates are written into LTS_Integration - the Shipment Details page and
/// the Excel upload both go through here, so permission checks, auditing and status/KPI
/// recalculation cannot be bypassed by adding a new caller.
/// </summary>
public interface IIntegrationMilestoneService
{
    Task<MilestoneApplyResult> ApplyAsync(
        IEnumerable<MilestoneChange> changes,
        MilestoneApplyOptions options,
        UserPermissions permissions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes and persists one shipment's KPI deadlines/Performance now, independent of any
    /// milestone-date change - for callers (e.g. ExportAttributeFeedRunner) that update a
    /// shipment's KPI-scoping attributes through a path other than ApplyAsync and need it rescored
    /// immediately against its now possibly-different target. No-ops if no shipment matches
    /// referenceNo.
    /// </summary>
    Task RecomputeKpiForShipmentAsync(string referenceNo, CancellationToken cancellationToken = default);
}

/// <summary>
/// Supplies the KPI targets used to score shipments. Cached, because the target list is small
/// and read on every grid render, and invalidated whenever an admin edits or imports targets.
/// </summary>
public interface IKpiTargetProvider
{
    Task<KpiTargetResolver> GetResolverAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops the cached targets after an edit or Excel import.</summary>
    void Invalidate();
}
