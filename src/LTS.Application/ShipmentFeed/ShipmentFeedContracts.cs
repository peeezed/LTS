using LTS.Application.Reference;

namespace LTS.Application.ShipmentFeed;

/// <summary>
/// One entry from the bulk "list shipment references for a country" call - the first of the 3-4
/// calls needed to fill in a complete shipment. TBD: exact field names, and whether it carries
/// anything beyond ReferenceNo, once the real payload is known.
/// </summary>
public sealed record ShipmentReferenceDto
{
    public string? ReferenceNo { get; init; }
}

/// <summary>
/// TBD placeholder for the detail call that owns core header fields. Exact endpoint/field names
/// are not yet known - adjust this record and IShipmentFeedClient.FetchShipmentHeaderAsync
/// together; nothing downstream (combiner, standardizer, runner) needs to change shape.
/// </summary>
public sealed record ShipmentHeaderDetailDto
{
    public string? ReferenceNo { get; init; }
    public string? InvoiceNo { get; init; }
    public DateOnly? InvoiceDate { get; init; }
}

/// <summary>TBD placeholder for the detail call that owns the six shipment attribute codes.</summary>
public sealed record ShipmentAttributesDetailDto
{
    public string? ArrivalCustoms { get; init; }
    public string? ExportType { get; init; }
    public string? TransportType { get; init; }
    public string? LoadingPoint { get; init; }
    public string? LogisticsCompany { get; init; }
    public string? BrokerCompany { get; init; }
}

/// <summary>TBD placeholder for the detail call that owns box/item/transfer counts.</summary>
public sealed record ShipmentCountsDetailDto
{
    public int? TotalTransfers { get; init; }
    public int? TotalBoxes { get; init; }
    public int? TotalItems { get; init; }
}

/// <summary>
/// One shipment's fields after ShipmentFeedCombiner has merged its list entry and every detail
/// call that succeeded. What ShipmentStandardizer consumes - it never sees the individual
/// per-endpoint pieces.
/// </summary>
public sealed record RawShipmentFeedDto
{
    public string? ReferenceNo { get; init; }
    public string? InvoiceNo { get; init; }
    public DateOnly? InvoiceDate { get; init; }
    public string? ArrivalCustoms { get; init; }
    public string? ExportType { get; init; }
    public string? TransportType { get; init; }
    public string? LoadingPoint { get; init; }
    public string? LogisticsCompany { get; init; }
    public string? BrokerCompany { get; init; }
    public int? TotalTransfers { get; init; }
    public int? TotalBoxes { get; init; }
    public int? TotalItems { get; init; }
}

/// <summary>Every LTS_Shipments field this module writes, plus any warnings raised while resolving them.</summary>
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
    int? TotalTransfers,
    int? TotalBoxes,
    int? TotalItems,
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
    /// The Description matching a raw code, or the raw code itself (plus a warning) when nothing
    /// matches - the same fallback-to-raw-text convention IntegrationShipmentQueryService's
    /// read-time resolver already uses.
    /// </summary>
    public string? Resolve(AttributeKind kind, string? rawCode, ICollection<string> warnings)
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

        warnings.Add($"{kind}: code '{rawCode}' not found in the lookup table; storing the raw code as a fallback.");
        return rawCode;
    }
}
