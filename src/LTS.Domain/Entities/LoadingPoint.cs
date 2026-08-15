using LTS.Domain.Common;

namespace LTS.Domain.Entities;

/// <summary>
/// Where a shipment is loaded. Each loading point belongs to a country, and that country is
/// what KPI targets are matched on — targets are given per loading country, not per point.
/// </summary>
public class LoadingPoint : Entity, IActivatable
{
    public required string Code { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// ISO alpha-2 code of the country this point loads from. Held as a code rather than a
    /// <see cref="Country"/> reference because shipments can load in countries that LTS does
    /// not itself operate in.
    /// </summary>
    public required string CountryCode { get; set; }

    public bool IsActive { get; set; } = true;

    public override string ToString() => $"{Name} ({CountryCode})";
}
