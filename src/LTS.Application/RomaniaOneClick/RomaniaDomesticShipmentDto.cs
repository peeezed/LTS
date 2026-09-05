namespace LTS.Application.RomaniaOneClick;

/// <summary>
/// The subset of one KLG OneClick "domestic-shipments" record this integration reads - one KLG
/// shipment corresponds to one LTS transfer. ShipmentDate is carried through but intentionally
/// never applied by RomaniaMilestoneMapper - it maps to the shipment-scope Crossdock Arrival
/// milestone, which this per-transfer feed deliberately leaves alone for now.
/// </summary>
public sealed record RomaniaDomesticShipmentDto(
    string PermShipmentId,
    string? ShipmentStatus,
    DateOnly? ShipmentDate,
    DateOnly? LoadingActStartDate,
    DateOnly? UnloadingStartDate,
    DateOnly? UnloadingActStartDate);
