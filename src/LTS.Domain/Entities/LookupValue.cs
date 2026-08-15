using LTS.Domain.Common;
using LTS.Domain.Enums;

namespace LTS.Domain.Entities;

/// <summary>
/// A value in one of the simple shipment attribute lists (arrival customs, export type,
/// transport type). One table for all three so a new country's values are rows, not migrations.
/// </summary>
public class LookupValue : Entity, IActivatable
{
    public required LookupKind Kind { get; set; }

    /// <summary>
    /// Restricts the value to one country. Null means it is available everywhere — export
    /// types are usually global, arrival customs offices are country-specific.
    /// </summary>
    public int? CountryId { get; set; }

    public Country? Country { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public override string ToString() => Name;
}
