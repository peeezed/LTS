using LTS.Application.Security;

namespace LTS.Application.Tracking;

/// <summary>
/// Read side of the tracking pages. Every method takes the country and the caller's permissions
/// and applies both before anything else, so a page cannot accidentally query unscoped.
/// </summary>
public interface IShipmentQueryService
{
    Task<PagedResult<ShipmentRow>> GetShipmentsAsync(
        int countryId,
        UserPermissions permissions,
        ShipmentFilter filter,
        GridRequest request,
        CancellationToken cancellationToken = default);

    Task<PagedResult<TransferRow>> GetTransfersAsync(
        int countryId,
        UserPermissions permissions,
        ShipmentFilter filter,
        GridRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A single shipment with its milestone dates, for the details page. Returns null when the
    /// reference does not exist or falls outside the caller's scope — the two are deliberately
    /// indistinguishable to the caller.
    /// </summary>
    Task<ShipmentDetail?> GetShipmentDetailAsync(
        int countryId,
        UserPermissions permissions,
        string reference,
        CancellationToken cancellationToken = default);

    /// <summary>Everything the Shipments On The Way dashboard needs, in one round trip.</summary>
    Task<InTransitSummary> GetInTransitSummaryAsync(
        int countryId,
        UserPermissions permissions,
        CancellationToken cancellationToken = default);
}
