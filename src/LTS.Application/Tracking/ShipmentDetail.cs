using LTS.Domain.Enums;

namespace LTS.Application.Tracking;

/// <summary>
/// One shipment as shown on the Shipment Details page: the header, its own milestone dates and
/// the transfers underneath it, whose crossdock and store dates can also be entered by hand.
/// </summary>
public sealed record ShipmentDetail
{
    public required int Id { get; init; }
    public required int ArrivalCountryId { get; init; }
    public required string ReferenceNo { get; init; }
    public required string InvoiceNo { get; init; }
    public DateOnly InvoiceDate { get; init; }

    public string? ArrivalCountry { get; init; }
    public string? ArrivalCustoms { get; init; }
    public string? ExportType { get; init; }
    public string? TransportType { get; init; }
    public string? LoadingPoint { get; init; }
    public string? LogisticsCompany { get; init; }
    public string? Broker { get; init; }

    public TrackingStatus CurrentStatus { get; init; }
    public PerformanceStatus Performance { get; init; }

    public int TransferCount { get; init; }
    public int TotalBoxes { get; init; }
    public int TotalItems { get; init; }

    /// <summary>The shipment's own milestone dates, keyed by type.</summary>
    public required IReadOnlyDictionary<MilestoneType, DateOnly?> Milestones { get; init; }

    public required IReadOnlyList<TransferDetail> Transfers { get; init; }
}

/// <summary>A transfer as shown underneath its shipment on the details page.</summary>
public sealed record TransferDetail
{
    public required int Id { get; init; }
    public required string TransferNo { get; init; }
    public required string Receiver { get; init; }

    public int TotalBoxes { get; init; }
    public int TotalItems { get; init; }

    public TrackingStatus CurrentStatus { get; init; }
    public PerformanceStatus Performance { get; init; }

    public required IReadOnlyDictionary<MilestoneType, DateOnly?> Milestones { get; init; }
}
