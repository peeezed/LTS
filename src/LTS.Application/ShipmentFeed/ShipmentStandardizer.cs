using LTS.Application.Reference;
using LTS.Domain.Enums;
using LTS.Domain.Services;

namespace LTS.Application.ShipmentFeed;

/// <summary>
/// Turns one combined shipment feed record into the exact set of LTS_Shipments field values this
/// module writes. Pure - no database, no HTTP - so it's testable with hand-built lookups.
/// </summary>
public static class ShipmentStandardizer
{
    public static StandardizedShipmentFields Standardize(RawShipmentFeedDto raw, AttributeCodeLookups lookups)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(lookups);

        if (string.IsNullOrWhiteSpace(raw.ReferenceNo))
        {
            throw new InvalidOperationException("Shipment feed record has no ReferenceNo.");
        }

        var warnings = new List<string>();

        return new StandardizedShipmentFields(
            raw.ReferenceNo,
            raw.InvoiceNo ?? string.Empty,
            raw.InvoiceDate ?? default,
            lookups.Resolve(AttributeKind.ArrivalCustoms, raw.ArrivalCustoms, warnings),
            lookups.Resolve(AttributeKind.ExportType, raw.ExportType, warnings),
            lookups.Resolve(AttributeKind.TransportType, raw.TransportType, warnings),
            lookups.Resolve(AttributeKind.LoadingPoint, raw.LoadingPoint, warnings),
            lookups.Resolve(AttributeKind.LogisticsCompany, raw.LogisticsCompany, warnings),
            lookups.Resolve(AttributeKind.Broker, raw.BrokerCompany, warnings),
            raw.TotalTransfers,
            raw.TotalBoxes,
            raw.TotalItems,
            warnings);
    }
}

/// <summary>
/// The "nothing has happened yet" CurrentStatus/Performance a brand-new shipment row gets,
/// derived the same way status is derived everywhere else rather than hardcoded strings.
/// </summary>
public static class ShipmentFeedDefaults
{
    public static (string CurrentStatus, string Performance) ForNewShipment()
    {
        var (status, _) = TrackingStatusCalculator.ForShipment(_ => null);
        return (status.ToDisplay(), PerformanceStatus.NotStarted.ToDisplay());
    }
}
