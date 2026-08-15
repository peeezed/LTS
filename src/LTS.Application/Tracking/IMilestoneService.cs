using LTS.Application.Security;
using LTS.Domain.Kpi;

namespace LTS.Application.Tracking;

/// <summary>
/// The one way milestone dates are written. Manual entry, Excel upload and the integration
/// poller all go through here, so permission checks, auditing and status/KPI recalculation
/// cannot be bypassed by adding a new caller.
/// </summary>
public interface IMilestoneService
{
    /// <summary>
    /// Applies a batch of changes, resolving each reference to its shipment or transfer.
    /// Changes are validated individually: a bad row is reported and the rest still apply.
    /// </summary>
    Task<MilestoneApplyResult> ApplyAsync(
        IEnumerable<MilestoneChange> changes,
        MilestoneApplyOptions options,
        UserPermissions permissions,
        CancellationToken cancellationToken = default);
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
