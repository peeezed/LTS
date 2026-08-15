using LTS.Domain.Common;
using LTS.Domain.Enums;
using LTS.Domain.Milestones;

namespace LTS.Domain.Entities;

/// <summary>
/// A consignment travelling from a loading point to the receiving country's crossdock.
/// Its life ends at crossdock arrival, where it is split into one <see cref="Transfer"/> per store.
/// </summary>
public class Shipment : Entity, IAuditable
{
    // --- Identity -----------------------------------------------------------------------
    // Both are unique across the whole system, not per country: one reference number is one
    // shipment, so imports and integration payloads can identify it without a country hint.

    public required string ReferenceNo { get; set; }

    public required string InvoiceNo { get; set; }

    public DateOnly InvoiceDate { get; set; }

    // --- The seven shipment attributes -------------------------------------------------
    // ArrivalCountry doubles as the operating country: it is what the user selects at login
    // and what every page and query is scoped to, so it is deliberately not duplicated.

    public required int ArrivalCountryId { get; set; }
    public Country? ArrivalCountry { get; set; }

    public int? ArrivalCustomsId { get; set; }
    public LookupValue? ArrivalCustoms { get; set; }

    public int? ExportTypeId { get; set; }
    public LookupValue? ExportType { get; set; }

    public int? TransportTypeId { get; set; }
    public LookupValue? TransportType { get; set; }

    public int? LoadingPointId { get; set; }
    public LoadingPoint? LoadingPoint { get; set; }

    public int? LogisticsCompanyId { get; set; }
    public Partner? LogisticsCompany { get; set; }

    public int? BrokerId { get; set; }
    public Partner? Broker { get; set; }

    // --- Milestone dates ----------------------------------------------------------------

    public DateOnly? LoadingDate { get; set; }
    public DateOnly? DepartureCustomsClearanceDate { get; set; }
    public DateOnly? DepartureDate { get; set; }
    public DateOnly? ArrivalToTargetCountryDate { get; set; }
    public DateOnly? CustomsStartDate { get; set; }
    public DateOnly? CustomsEndDate { get; set; }
    public DateOnly? CrossdockArrivalDate { get; set; }

    // --- Rollups from the transfer split -------------------------------------------------

    public int TransferCount { get; set; }
    public int TotalBoxes { get; set; }
    public int TotalItems { get; set; }

    // --- Denormalised for grid sorting and filtering ------------------------------------
    // Recalculated on every write by ShipmentStatusCalculator so the grids can order by these
    // in SQL instead of scoring thousands of rows in memory.

    public TrackingStatus CurrentStatus { get; set; } = TrackingStatus.Created;

    public DateOnly? CurrentStatusDate { get; set; }

    public PerformanceStatus Performance { get; set; } = PerformanceStatus.NotStarted;

    public ICollection<Transfer> Transfers { get; set; } = [];

    // --- Auditing -------------------------------------------------------------------------

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>The loading country used for KPI matching, taken from the loading point.</summary>
    public string? LoadingCountryCode => LoadingPoint?.CountryCode;

    /// <summary>Reads a milestone date by type. Throws for milestones that live on the transfer.</summary>
    public DateOnly? GetMilestoneDate(MilestoneType type) => type switch
    {
        MilestoneType.Loading => LoadingDate,
        MilestoneType.DepartureCustomsClearance => DepartureCustomsClearanceDate,
        MilestoneType.Departure => DepartureDate,
        MilestoneType.ArrivalToTargetCountry => ArrivalToTargetCountryDate,
        MilestoneType.CustomsStart => CustomsStartDate,
        MilestoneType.CustomsEnd => CustomsEndDate,
        MilestoneType.CrossdockArrival => CrossdockArrivalDate,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a shipment milestone.")
    };

    /// <summary>Writes a milestone date by type. Throws for milestones that live on the transfer.</summary>
    public void SetMilestoneDate(MilestoneType type, DateOnly? value)
    {
        switch (type)
        {
            case MilestoneType.Loading: LoadingDate = value; break;
            case MilestoneType.DepartureCustomsClearance: DepartureCustomsClearanceDate = value; break;
            case MilestoneType.Departure: DepartureDate = value; break;
            case MilestoneType.ArrivalToTargetCountry: ArrivalToTargetCountryDate = value; break;
            case MilestoneType.CustomsStart: CustomsStartDate = value; break;
            case MilestoneType.CustomsEnd: CustomsEndDate = value; break;
            case MilestoneType.CrossdockArrival: CrossdockArrivalDate = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(type), type, "Not a shipment milestone.");
        }
    }

    /// <summary>All shipment milestone dates, for KPI evaluation.</summary>
    public Dictionary<MilestoneType, DateOnly?> GetMilestoneDates() =>
        MilestoneCatalog.ShipmentMilestones.ToDictionary(d => d.Type, d => GetMilestoneDate(d.Type));

    public override string ToString() => ReferenceNo;
}
