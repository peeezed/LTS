using LTS.Domain.Enums;

namespace LTS.Application.Tracking;

/// <summary>One page of results plus the total, which the grids need for their pager.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount)
{
    public static PagedResult<T> Empty { get; } = new([], 0);
}

/// <summary>
/// Filters shared by the Shipments, Transfers and Shipments On The Way pages. Every field is
/// optional; the country and the user's partner scope are applied separately and are not
/// negotiable.
/// </summary>
public sealed record ShipmentFilter
{
    /// <summary>Matches reference number, invoice number or transfer number.</summary>
    public string? Search { get; init; }

    public IReadOnlyCollection<TrackingStatus>? Statuses { get; init; }

    public IReadOnlyCollection<PerformanceStatus>? Performances { get; init; }

    public int? ArrivalCustomsId { get; init; }
    public int? ExportTypeId { get; init; }
    public int? TransportTypeId { get; init; }
    public int? LoadingPointId { get; init; }

    /// <summary>LTS_LogisticsCompanies/LTS_Brokers Code - see AttributeKind.LogisticsCompany.</summary>
    public string? LogisticsCompanyCode { get; init; }

    /// <summary>LTS_LogisticsCompanies/LTS_Brokers Code - see AttributeKind.Broker.</summary>
    public string? BrokerCode { get; init; }

    public DateOnly? InvoiceDateFrom { get; init; }
    public DateOnly? InvoiceDateTo { get; init; }

    /// <summary>
    /// Restricts results to shipments that have not reached the store yet — what the
    /// "Shipments On The Way" page shows.
    /// </summary>
    public bool OnlyInTransit { get; init; }
}

/// <summary>Sorting and paging for a grid request.</summary>
public sealed record GridRequest(
    int Page = 0,
    int PageSize = 50,
    string? SortBy = null,
    bool SortDescending = false)
{
    public int Skip => Page * PageSize;
}

/// <summary>A row of the Shipments grid: the seven attributes, the dates and the scores.</summary>
public sealed record ShipmentRow
{
    public required int Id { get; init; }
    public required string ReferenceNo { get; init; }
    public required string InvoiceNo { get; init; }
    public DateOnly InvoiceDate { get; init; }

    public string? ArrivalCountry { get; init; }
    public string? ArrivalCustoms { get; init; }
    public string? ExportType { get; init; }
    public string? TransportType { get; init; }
    public string? LoadingPoint { get; init; }
    public string? LoadingCountryCode { get; init; }
    public string? LogisticsCompany { get; init; }
    public string? Broker { get; init; }

    public DateOnly? LoadingDate { get; init; }
    public DateOnly? DepartureCustomsClearanceDate { get; init; }
    public DateOnly? DepartureDate { get; init; }
    public DateOnly? ArrivalToTargetCountryDate { get; init; }
    public DateOnly? CustomsStartDate { get; init; }
    public DateOnly? CustomsEndDate { get; init; }
    public DateOnly? CrossdockArrivalDate { get; init; }

    public int TransferCount { get; init; }
    public int TotalBoxes { get; init; }
    public int TotalItems { get; init; }

    public TrackingStatus CurrentStatus { get; init; }
    public DateOnly? CurrentStatusDate { get; init; }
    public PerformanceStatus Performance { get; init; }
}

/// <summary>A row of the Transfers grid — the store leg of a shipment.</summary>
public sealed record TransferRow
{
    public required int Id { get; init; }
    public required int ShipmentId { get; init; }
    public required string TransferNo { get; init; }
    public required string ReferenceNo { get; init; }
    public required string InvoiceNo { get; init; }

    /// <summary>Date created: the shipment's invoice date.</summary>
    public DateOnly DateCreated { get; init; }

    public string? StoreCode { get; init; }
    public string? StoreName { get; init; }

    /// <summary>Receiver as shown in the grid: "code - name".</summary>
    public string Receiver => $"{StoreCode} - {StoreName}";

    public TrackingStatus CurrentStatus { get; init; }
    public PerformanceStatus Performance { get; init; }

    public int TotalBoxes { get; init; }
    public int TotalItems { get; init; }

    public DateOnly? CrossdockDepartureDate { get; init; }
    public DateOnly? PlannedStoreArrivalDate { get; init; }
    public DateOnly? StoreArrivalDate { get; init; }
    public DateOnly? StorePreAcceptanceDate { get; init; }
    public DateOnly? StoreAcceptanceDate { get; init; }
}
