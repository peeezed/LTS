using LTS.Domain.Common;
using LTS.Domain.Enums;

namespace LTS.Domain.Entities;

/// <summary>
/// Translates one source system's status code into an LTS milestone. This is what makes the
/// integration layer invisible: whatever code a country's system emits, an admin decides here
/// which standard status it means, and remapping is a data edit rather than a release.
/// </summary>
public class StatusMapping : Entity, IAuditable, IActivatable
{
    public required int IntegrationSourceId { get; set; }
    public IntegrationSource? IntegrationSource { get; set; }

    /// <summary>The code exactly as the source system sends it. Matched case-insensitively.</summary>
    public required string RawCode { get; set; }

    /// <summary>Human-readable meaning of the raw code, shown in the admin grid.</summary>
    public string? RawDescription { get; set; }

    /// <summary>The LTS milestone this code sets. Null parks the code as deliberately ignored.</summary>
    public MilestoneType? MilestoneType { get; set; }

    /// <summary>
    /// When true the code is recognised but intentionally dropped, which keeps it out of the
    /// "unmapped code" warnings on the integration monitor.
    /// </summary>
    public bool IsIgnored { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
