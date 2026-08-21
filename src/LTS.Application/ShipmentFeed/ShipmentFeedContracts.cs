using LTS.Application.Reference;

namespace LTS.Application.ShipmentFeed;

/// <summary>Envelope every call to the company's own internal API wraps its payload in.</summary>
public sealed record ApiEnvelope<T>(bool IsSuccess, T? Value, string? Message);

/// <summary>
/// One entry from GetInvoiceListByCustomerCode - one bulk call per country, returning the full
/// shipment header AND all six attribute codes (with the source's own resolved description text
/// alongside each) in one shot. Status and eInvoiceNumber are carried for the raw staged JSON's
/// sake only - neither is written to any LTS_Shipments column (per the user, the app computes its
/// own CurrentStatus from milestones; this ERP-side status isn't used for anything).
/// </summary>
public sealed record InvoiceListEntryDto(
    string InvoiceNumber,
    DateTimeOffset InvoiceDate,
    string ExportNumber,
    string? ERPTransferWarehouseCode,
    string? ERPTransferWarehouseDescription,
    string? Arrival_Customs,
    string? Arrival_Customs_Desc,
    string? Export_Type,
    string? Export_Type_Desc,
    string? Transport,
    string? Transport_Desc,
    string? Loading_Point,
    string? Loading_Point_Desc,
    string? Carier,
    string? Carier_Desc,
    string? Broker_Company,
    string? Broker_Company_Desc,
    int Status,
    string? eInvoiceNumber);

/// <summary>
/// One line from GetInvoiceDetailByInvoiceNumber - one product/box/store combination. Only
/// PackageNumber, Quantity, StoreCode and ExportNumber are used, per the user: "we don't need much
/// of the info, we just want to know how many boxes, box names and how many products are in them,
/// and the store it is going to." The rest is kept on the DTO only because it's staged verbatim,
/// not because anything downstream reads it.
/// </summary>
public sealed record InvoiceDetailLineDto(
    string InvoiceNumber,
    DateTimeOffset InvoiceDate,
    string? ShippingNumber,
    string PackageNumber,
    string? OptionCode,
    string? Barcode,
    string? SizeCode,
    decimal Quantity,
    decimal Amount,
    string StoreCode,
    string? CurrencyCode,
    string ExportNumber,
    string? PackageDimension,
    string? TotalPackageWeight);

/// <summary>One shipment's list entry plus every detail line returned for it, before grouping into transfers/boxes.</summary>
public sealed record RawShipmentFeedDto(InvoiceListEntryDto Header, IReadOnlyList<InvoiceDetailLineDto> DetailLines);

/// <summary>One box within a transfer - just its name (PackageNumber) and how many units are in it.</summary>
public sealed record StandardizedBoxFields(string PackageNo, decimal ProductCount);

/// <summary>One transfer - one per distinct destination store found in the shipment's detail lines.</summary>
public sealed record StandardizedTransferFields(
    string TransferNo,
    string ReceivingStoreCode,
    IReadOnlyList<StandardizedBoxFields> Boxes,
    int TotalBoxes,
    int TotalItems);

/// <summary>Everything LTS_Shipments (and, via Transfers, LTS_ShipmentTransfers/LTS_Boxes) gets written from.</summary>
public sealed record StandardizedShipmentFields(
    string ReferenceNo,
    string InvoiceNo,
    DateOnly InvoiceDate,
    string? ArrivalCustoms,
    string? ExportType,
    string? TransportType,
    string? LoadingPoint,
    string? LogisticsCompany,
    string? BrokerCompany,
    IReadOnlyList<StandardizedTransferFields> Transfers,
    int TotalTransfers,
    int TotalBoxes,
    int TotalItems,
    IReadOnlyList<string> Warnings);

/// <summary>
/// The six shipment attribute lookup tables, Code-keyed - the mirror image of
/// IntegrationShipmentQueryService's Description-keyed AttributeLookups, since this module writes
/// LTS_Shipments (which stores Description text) from raw API codes, the opposite direction of
/// that service's read-time "Code - Description" display resolution.
/// </summary>
public sealed record AttributeCodeLookups(
    IReadOnlyDictionary<string, string> ArrivalCustoms,
    IReadOnlyDictionary<string, string> ExportType,
    IReadOnlyDictionary<string, string> TransportType,
    IReadOnlyDictionary<string, string> LoadingPoint,
    IReadOnlyDictionary<string, string> LogisticsCompany,
    IReadOnlyDictionary<string, string> Broker)
{
    /// <summary>
    /// The Description matching a raw code in our own lookup table, or - when nothing matches -
    /// the source's own description text for that field (better than the bare code), plus a
    /// warning so an admin can add the missing code to the lookup table later.
    /// </summary>
    public string? Resolve(AttributeKind kind, string? rawCode, string? sourceDescription, ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return null;
        }

        var table = kind switch
        {
            AttributeKind.ArrivalCustoms => ArrivalCustoms,
            AttributeKind.ExportType => ExportType,
            AttributeKind.TransportType => TransportType,
            AttributeKind.LoadingPoint => LoadingPoint,
            AttributeKind.LogisticsCompany => LogisticsCompany,
            AttributeKind.Broker => Broker,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        if (table.TryGetValue(rawCode, out var description))
        {
            return description;
        }

        warnings.Add($"{kind}: code '{rawCode}' not found in the lookup table; falling back to the source's own description.");
        return string.IsNullOrWhiteSpace(sourceDescription) ? rawCode : sourceDescription;
    }
}
