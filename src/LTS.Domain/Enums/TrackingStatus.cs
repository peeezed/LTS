namespace LTS.Domain.Enums;

/// <summary>
/// The shipment/transfer lifecycle position. Ordered: the current status is the highest
/// value whose milestone has a date, so the enum can be sorted and filtered directly in SQL.
/// Shipments stop at <see cref="AtCrossdock"/>; transfers continue from there.
/// </summary>
public enum TrackingStatus
{
    Created = 0,
    Loaded = 10,
    ExportCustomsCleared = 20,
    Departed = 30,
    ArrivedInTargetCountry = 40,
    CustomsInProgress = 50,
    CustomsCleared = 60,
    AtCrossdock = 70,
    InTransitToStore = 80,
    ArrivedAtStore = 90,
    PreAccepted = 100,
    Accepted = 110
}
