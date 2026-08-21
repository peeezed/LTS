namespace LTS.Application.ShipmentFeed;

/// <summary>
/// Turns one shipment's list entry and detail lines into the combined record ShipmentStandardizer
/// expects, and groups those detail lines into transfers (one per destination store) and boxes
/// (one per package number within a store) - the real logic here, since the two API calls this
/// module makes don't line up with LTS_Integration's shipment/transfer/box shape on their own.
/// </summary>
public static class ShipmentFeedCombiner
{
    public static RawShipmentFeedDto Combine(InvoiceListEntryDto header, IReadOnlyList<InvoiceDetailLineDto> detailLines)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(detailLines);

        return new RawShipmentFeedDto(header, detailLines);
    }

    /// <summary>
    /// Groups detail lines by StoreCode into one transfer per store (TransferNo synthesized as
    /// "{referenceNo}_{StoreCode}" - the source has no ready-made per-store transfer number), and
    /// within each store, by PackageNumber into one box per package. TotalItems sums Quantity
    /// (units), not distinct line count, per the user.
    /// </summary>
    public static IReadOnlyList<StandardizedTransferFields> GroupIntoTransfers(
        string referenceNo, IReadOnlyList<InvoiceDetailLineDto> lines) =>
        lines
            .GroupBy(l => l.StoreCode)
            .Select(storeGroup =>
            {
                var boxes = storeGroup
                    .GroupBy(l => l.PackageNumber)
                    .Select(boxGroup => new StandardizedBoxFields(boxGroup.Key, boxGroup.Sum(l => l.Quantity)))
                    .ToList();

                return new StandardizedTransferFields(
                    TransferNo: $"{referenceNo}_{storeGroup.Key}",
                    ReceivingStoreCode: storeGroup.Key,
                    Boxes: boxes,
                    TotalBoxes: boxes.Count,
                    TotalItems: (int)Math.Round(boxes.Sum(b => b.ProductCount), MidpointRounding.AwayFromZero));
            })
            .ToList();
}
