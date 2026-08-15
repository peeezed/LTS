using LTS.Domain.Common;
using LTS.Domain.Enums;

namespace LTS.Domain.Entities;

/// <summary>
/// Append-only record of every milestone date change. Because an integration can overwrite a
/// manually entered value, the previous value has to survive somewhere — this is that place,
/// and it is also what the Audit Log admin page reads.
/// </summary>
public class MilestoneAudit : Entity
{
    public required MilestoneScope Scope { get; set; }

    /// <summary>Id of the shipment or transfer the change was made against.</summary>
    public required int EntityId { get; set; }

    /// <summary>
    /// Always the owning shipment, including for transfer-scoped changes, so the whole history
    /// of a reference number can be pulled with one query.
    /// </summary>
    public required int ShipmentId { get; set; }

    public required MilestoneType MilestoneType { get; set; }

    public DateOnly? OldValue { get; set; }

    public DateOnly? NewValue { get; set; }

    public required MilestoneSource Source { get; set; }

    /// <summary>Account that made the change; null for integration and seed writes.</summary>
    public Guid? UserId { get; set; }

    public string? UserName { get; set; }

    /// <summary>Partner the user acted on behalf of, for external accounts.</summary>
    public int? PartnerId { get; set; }

    /// <summary>The integration poll that produced the change, when the source was an integration.</summary>
    public int? IntegrationRunId { get; set; }

    public DateTime ChangedAt { get; set; }

    /// <summary>Free text, e.g. the Excel file name for an upload or the raw status code for an integration.</summary>
    public string? Note { get; set; }
}
