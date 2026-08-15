using LTS.Domain.Common;
using LTS.Domain.Enums;

namespace LTS.Domain.Entities;

/// <summary>
/// One external system LTS pulls from, for one country. Onboarding a country means adding
/// rows here plus its status mappings — the adapter is selected by <see cref="AdapterKey"/>,
/// so no domain, UI or KPI code changes.
/// </summary>
public class IntegrationSource : Entity, IAuditable, IActivatable
{
    public required int CountryId { get; set; }
    public Country? Country { get; set; }

    public required IntegrationSourceKind Kind { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// Selects the adapter implementation from the registry (e.g. "mock-json", "tr-warehouse").
    /// </summary>
    public required string AdapterKey { get; set; }

    public string? BaseUrl { get; set; }

    /// <summary>
    /// Name of the configuration/secret entry holding the credential. The secret itself is
    /// never stored in the database.
    /// </summary>
    public string? SecretName { get; set; }

    /// <summary>Free-form adapter settings as JSON, for anything not common to all sources.</summary>
    public string? SettingsJson { get; set; }

    public int PollIntervalMinutes { get; set; } = 15;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When true, a value already entered by hand is kept and the incoming value is only
    /// audited. Off by default: the source system is normally the authority.
    /// </summary>
    public bool ManualOverrideWins { get; set; }

    // --- Poll state -----------------------------------------------------------------------

    public DateTime? LastRunAt { get; set; }

    public DateTime? LastSuccessAt { get; set; }

    /// <summary>Opaque incremental cursor handed back to the adapter on the next poll.</summary>
    public string? Cursor { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public bool IsDue(DateTime utcNow) =>
        IsActive && (LastRunAt is null || LastRunAt.Value.AddMinutes(PollIntervalMinutes) <= utcNow);

    public override string ToString() => Name;
}
