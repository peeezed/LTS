using LTS.Application.Reference;
using LTS.Domain.Enums;
using LTS.Domain.Services;

namespace LTS.Application.ShipmentFeed;

/// <summary>
/// Turns one combined shipment feed record into the exact set of LTS_Shipments (plus its
/// transfers/boxes) field values this module writes. Pure - no database, no HTTP - so it's
/// testable with hand-built lookups.
/// </summary>
public static class ShipmentStandardizer
{
    public static StandardizedShipmentFields Standardize(RawShipmentFeedDto raw, AttributeCodeLookups lookups)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(lookups);

        var header = raw.Header;

        if (string.IsNullOrWhiteSpace(header.ExportNumber))
        {
            throw new InvalidOperationException("Shipment feed record has no ExportNumber.");
        }

        var warnings = new List<string>();
        var transfers = ShipmentFeedCombiner.GroupIntoTransfers(header.ExportNumber, raw.DetailLines);

        return new StandardizedShipmentFields(
            header.ExportNumber,
            header.InvoiceNumber,
            DateOnly.FromDateTime(header.InvoiceDate.Date),
            lookups.Resolve(AttributeKind.ArrivalCustoms, header.Arrival_Customs, header.Arrival_Customs_Desc, warnings),
            lookups.Resolve(AttributeKind.ExportType, header.Export_Type, header.Export_Type_Desc, warnings),
            lookups.Resolve(AttributeKind.TransportType, header.Transport, header.Transport_Desc, warnings),
            lookups.Resolve(AttributeKind.LoadingPoint, header.Loading_Point, header.Loading_Point_Desc, warnings),
            lookups.Resolve(AttributeKind.LogisticsCompany, header.Carier, header.Carier_Desc, warnings),
            lookups.Resolve(AttributeKind.Broker, header.Broker_Company, header.Broker_Company_Desc, warnings),
            transfers,
            transfers.Count,
            transfers.Sum(t => t.TotalBoxes),
            transfers.Sum(t => t.TotalItems),
            warnings);
    }
}

/// <summary>
/// The "nothing has happened yet" CurrentStatus/Performance a brand-new shipment or transfer row
/// gets, derived the same way status is derived everywhere else rather than hardcoded strings. A
/// new transfer always inherits its shipment's current (milestone-derived) status verbatim - this
/// module never writes transfer milestone dates, so ShipmentStatusAggregator.TransferStatus would
/// trivially return that same seed status straight back.
/// </summary>
public static class ShipmentFeedDefaults
{
    public static (TrackingStatus Status, string CurrentStatus, string Performance) ForNewShipment()
    {
        var (status, _) = TrackingStatusCalculator.ForShipment(_ => null);
        return (status, status.ToDisplay(), PerformanceStatus.NotStarted.ToDisplay());
    }

    public static (string CurrentStatus, string Performance) ForNewTransfer(TrackingStatus shipmentStatus) =>
        (shipmentStatus.ToDisplay(), PerformanceStatus.NotStarted.ToDisplay());
}
