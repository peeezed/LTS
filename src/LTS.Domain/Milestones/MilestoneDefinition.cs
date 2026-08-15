using LTS.Domain.Enums;

namespace LTS.Domain.Milestones;

/// <summary>
/// Everything the system needs to know about one milestone. The catalog of these is the single
/// source of truth: it drives the Shipment Details field groups, Excel upload templates and
/// validation, the status-mapping admin dropdown, and current-status calculation.
/// </summary>
/// <param name="Type">The milestone itself.</param>
/// <param name="Scope">Whether it lives on the shipment or on each transfer.</param>
/// <param name="Owner">Who is responsible for supplying the date.</param>
/// <param name="Sequence">Position in the lifecycle; also the natural display order.</param>
/// <param name="DisplayName">Label shown in grids, forms and Excel headers.</param>
/// <param name="ReachedStatus">
/// The status the entity reaches once this date exists, or <c>null</c> for milestones that do
/// not advance the lifecycle (a planned date is a forecast, not an achievement).
/// </param>
/// <param name="AllowsManualEntry">
/// False for milestones owned exclusively by the in-house service, which LTS only ever displays.
/// </param>
public sealed record MilestoneDefinition(
    MilestoneType Type,
    MilestoneScope Scope,
    MilestoneOwner Owner,
    int Sequence,
    string DisplayName,
    TrackingStatus? ReachedStatus,
    bool AllowsManualEntry)
{
    /// <summary>A planned/forecast date rather than a recorded event.</summary>
    public bool IsPlanned => ReachedStatus is null;
}
