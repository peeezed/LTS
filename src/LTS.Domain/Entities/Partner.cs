using LTS.Domain.Common;
using LTS.Domain.Enums;

namespace LTS.Domain.Entities;

/// <summary>
/// An external company: either a logistics company that moves shipments or a customs broker.
/// External user accounts belong to a partner, and that link is what restricts them to their
/// own shipments.
/// </summary>
public class Partner : Entity, IActivatable
{
    public required PartnerType Type { get; set; }

    /// <summary>Short code used in imports and integration payloads.</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    public bool IsActive { get; set; } = true;

    public override string ToString() => Name;
}
