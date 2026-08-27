using LTS.Application.Reference;
using LTS.Application.ShipmentFeed;

namespace LTS.Application.ExportAttributeFeed;

/// <summary>
/// Turns one ExportFileDetailDto into the resolved attribute fields this module writes, using the
/// same Code-&gt;Description resolution ShipmentFeed already established (AttributeCodeLookups,
/// reused here as a generic shared utility - this module otherwise shares no code or schedule with
/// ShipmentFeed). Carier/CarierDesc map onto AttributeKind.LogisticsCompany and
/// BrokerCompany/BrokerCompanyDesc onto AttributeKind.Broker, the same non-obvious convention
/// AttributeCodeLookupLoader already uses.
/// </summary>
public static class ExportAttributeStandardizer
{
    public static StandardizedExportAttributes Standardize(ExportFileDetailDto detail, AttributeCodeLookups lookups)
    {
        var warnings = new List<string>();

        return new StandardizedExportAttributes(
            ReferenceNo: detail.ExportFileNumber,
            ArrivalCustoms: lookups.Resolve(AttributeKind.ArrivalCustoms, detail.ArrivalCustoms, detail.ArrivalCustomsDesc, warnings),
            ExportType: lookups.Resolve(AttributeKind.ExportType, detail.ExportType, detail.ExportTypeDesc, warnings),
            TransportType: lookups.Resolve(AttributeKind.TransportType, detail.Transport, detail.TransportDesc, warnings),
            LoadingPoint: lookups.Resolve(AttributeKind.LoadingPoint, detail.LoadingPoint, detail.LoadingPointDesc, warnings),
            LogisticsCompany: lookups.Resolve(AttributeKind.LogisticsCompany, detail.Carier, detail.CarierDesc, warnings),
            BrokerCompany: lookups.Resolve(AttributeKind.Broker, detail.BrokerCompany, detail.BrokerCompanyDesc, warnings),
            Warnings: warnings);
    }
}
