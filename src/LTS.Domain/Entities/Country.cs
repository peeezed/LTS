using LTS.Domain.Common;

namespace LTS.Domain.Entities;

/// <summary>
/// A country LTS operates in. Users pick one after logging in and every page is scoped to it.
/// Adding a country is a data operation: insert the row, its reference data, and its
/// integration sources — no code changes.
/// </summary>
public class Country : Entity, IActivatable
{
    /// <summary>ISO 3166-1 alpha-2 code, used in URLs (e.g. <c>/TR/shipments</c>).</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When true, KPI durations skip weekends and this country's holidays. Off by default:
    /// LTS scores calendar days until a holiday calendar is maintained for the country.
    /// </summary>
    public bool UseWorkingDays { get; set; }

    public ICollection<Store> Stores { get; set; } = [];

    public override string ToString() => $"{Code} - {Name}";
}
