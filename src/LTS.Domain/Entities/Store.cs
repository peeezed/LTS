using LTS.Domain.Common;

namespace LTS.Domain.Entities;

/// <summary>
/// The final destination of a transfer. Shown in the Transfers grid as the receiver,
/// formatted "code - name".
/// </summary>
public class Store : Entity, IActivatable
{
    public required int CountryId { get; set; }

    public Country? Country { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Receiver label used in the Transfers grid.</summary>
    public string DisplayName => $"{Code} - {Name}";

    public override string ToString() => DisplayName;
}
