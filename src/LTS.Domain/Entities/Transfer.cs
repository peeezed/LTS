using LTS.Domain.Common;
using LTS.Domain.Enums;
using LTS.Domain.Milestones;

namespace LTS.Domain.Entities;

/// <summary>
/// One store's share of a shipment, created when the shipment is split at the crossdock.
/// Carries the second half of the lifecycle, from crossdock departure to store acceptance.
/// </summary>
public class Transfer : Entity, IAuditable
{
    public required int ShipmentId { get; set; }
    public Shipment? Shipment { get; set; }

    public required int StoreId { get; set; }
    public Store? Store { get; set; }

    /// <summary>
    /// "{ReferenceNo}_{StoreCode}". Persisted rather than computed so it can be indexed,
    /// searched and matched against integration payloads; built by <see cref="BuildTransferNo"/>.
    /// </summary>
    public required string TransferNo { get; set; }

    public int TotalBoxes { get; set; }
    public int TotalItems { get; set; }

    // --- Milestone dates ----------------------------------------------------------------

    public DateOnly? CrossdockDepartureDate { get; set; }

    /// <summary>Optional forecast from the warehouse; does not advance the status.</summary>
    public DateOnly? PlannedStoreArrivalDate { get; set; }

    public DateOnly? StoreArrivalDate { get; set; }
    public DateOnly? StorePreAcceptanceDate { get; set; }
    public DateOnly? StoreAcceptanceDate { get; set; }

    // --- Denormalised for grid sorting and filtering ------------------------------------

    public TrackingStatus CurrentStatus { get; set; } = TrackingStatus.AtCrossdock;

    public DateOnly? CurrentStatusDate { get; set; }

    public PerformanceStatus Performance { get; set; } = PerformanceStatus.NotStarted;

    // --- Auditing -------------------------------------------------------------------------

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public static string BuildTransferNo(string referenceNo, string storeCode) => $"{referenceNo}_{storeCode}";

    /// <summary>Reads a milestone date by type. Throws for milestones that live on the shipment.</summary>
    public DateOnly? GetMilestoneDate(MilestoneType type) => type switch
    {
        MilestoneType.CrossdockDeparture => CrossdockDepartureDate,
        MilestoneType.PlannedStoreArrival => PlannedStoreArrivalDate,
        MilestoneType.StoreArrival => StoreArrivalDate,
        MilestoneType.StorePreAcceptance => StorePreAcceptanceDate,
        MilestoneType.StoreAcceptance => StoreAcceptanceDate,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a transfer milestone.")
    };

    /// <summary>Writes a milestone date by type. Throws for milestones that live on the shipment.</summary>
    public void SetMilestoneDate(MilestoneType type, DateOnly? value)
    {
        switch (type)
        {
            case MilestoneType.CrossdockDeparture: CrossdockDepartureDate = value; break;
            case MilestoneType.PlannedStoreArrival: PlannedStoreArrivalDate = value; break;
            case MilestoneType.StoreArrival: StoreArrivalDate = value; break;
            case MilestoneType.StorePreAcceptance: StorePreAcceptanceDate = value; break;
            case MilestoneType.StoreAcceptance: StoreAcceptanceDate = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(type), type, "Not a transfer milestone.");
        }
    }

    /// <summary>
    /// The transfer's own dates merged with its parent shipment's, which KPI evaluation needs
    /// because steps such as Crossdock Arrival → Crossdock Departure straddle the two entities.
    /// </summary>
    public Dictionary<MilestoneType, DateOnly?> GetMilestoneDates(Shipment? shipment = null)
    {
        shipment ??= Shipment;

        var dates = shipment is null
            ? MilestoneCatalog.ShipmentMilestones.ToDictionary(d => d.Type, _ => (DateOnly?)null)
            : shipment.GetMilestoneDates();

        foreach (var definition in MilestoneCatalog.TransferMilestones)
        {
            dates[definition.Type] = GetMilestoneDate(definition.Type);
        }

        return dates;
    }

    public override string ToString() => TransferNo;
}
