namespace LTS.Application.RomaniaOneClick;

/// <summary>Writes the KLG perm_shipment_id a person types onto a Romania transfer (see Transfers.razor).</summary>
public interface IRomaniaTransferLinkService
{
    /// <summary>No-ops if no transfer matches transferNo. A blank/whitespace value clears the link.</summary>
    Task SetPermShipmentIdAsync(string transferNo, string? value, CancellationToken cancellationToken = default);
}
