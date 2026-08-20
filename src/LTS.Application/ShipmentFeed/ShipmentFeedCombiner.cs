namespace LTS.Application.ShipmentFeed;

/// <summary>
/// Merges one shipment's list entry and up to three detail-call results into the single combined
/// record ShipmentStandardizer expects. Each endpoint owns a distinct slice of fields, so this is
/// a straightforward copy rather than conflict resolution - written defensively (a later,
/// non-null piece wins) in case a field turns out to be sent by more than one endpoint once the
/// real API spec is known. A null detail (that call failed, 404'd, or hasn't been implemented
/// yet) just leaves its fields blank in the combined record rather than failing the whole merge.
/// </summary>
public static class ShipmentFeedCombiner
{
    public static RawShipmentFeedDto Combine(
        ShipmentReferenceDto reference,
        ShipmentHeaderDetailDto? header,
        ShipmentAttributesDetailDto? attributes,
        ShipmentCountsDetailDto? counts)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return new RawShipmentFeedDto
        {
            ReferenceNo = header?.ReferenceNo ?? reference.ReferenceNo,
            InvoiceNo = header?.InvoiceNo,
            InvoiceDate = header?.InvoiceDate,
            ArrivalCustoms = attributes?.ArrivalCustoms,
            ExportType = attributes?.ExportType,
            TransportType = attributes?.TransportType,
            LoadingPoint = attributes?.LoadingPoint,
            LogisticsCompany = attributes?.LogisticsCompany,
            BrokerCompany = attributes?.BrokerCompany,
            TotalTransfers = counts?.TotalTransfers,
            TotalBoxes = counts?.TotalBoxes,
            TotalItems = counts?.TotalItems
        };
    }
}
