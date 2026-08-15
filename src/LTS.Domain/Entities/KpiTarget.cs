using LTS.Domain.Common;
using LTS.Domain.Enums;

namespace LTS.Domain.Entities;

/// <summary>
/// A target duration in days for one lifecycle step, as supplied by the logistics department.
/// Matched on export type + loading country + arrival country; any key left null means "any",
/// so a broad fallback row can sit underneath specific ones.
/// </summary>
public class KpiTarget : Entity, IAuditable, IActivatable
{
    public required KpiStep Step { get; set; }

    /// <summary>Shipment type this target applies to. Null matches any export type.</summary>
    public int? ExportTypeId { get; set; }
    public LookupValue? ExportType { get; set; }

    /// <summary>
    /// ISO alpha-2 code of the loading country, taken from the shipment's loading point.
    /// Null matches any loading country.
    /// </summary>
    public string? LoadingCountryCode { get; set; }

    /// <summary>Receiving country. Null matches any arrival country.</summary>
    public int? ArrivalCountryId { get; set; }
    public Country? ArrivalCountry { get; set; }

    /// <summary>Target duration in days for the step.</summary>
    public required int TargetDays { get; set; }

    /// <summary>
    /// Targets are versioned by date so a revised KPI does not rewrite history: a shipment is
    /// scored against the target that was in force on its loading date.
    /// </summary>
    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// How many of the three keys this row pins down. The highest-scoring matching row wins,
    /// so "Definitive / DE / TR" beats "Definitive / any / TR" beats "any / any / any".
    /// </summary>
    public int Specificity =>
        (ExportTypeId.HasValue ? 1 : 0) +
        (LoadingCountryCode is not null ? 1 : 0) +
        (ArrivalCountryId.HasValue ? 1 : 0);

    /// <summary>True when this target is in force on the given date.</summary>
    public bool IsEffectiveOn(DateOnly date) =>
        IsActive && EffectiveFrom <= date && (EffectiveTo is null || EffectiveTo >= date);
}
