using FluentAssertions;
using LTS.Application.ShipmentFeed;
using LTS.Domain.Enums;

namespace LTS.Tests.ShipmentFeed;

public class ShipmentStandardizerTests
{
    private static AttributeCodeLookups Lookups() => new(
        ArrivalCustoms: new Dictionary<string, string> { ["AC001"] = "Moscow" },
        ExportType: new Dictionary<string, string> { ["ET003"] = "Export" },
        TransportType: new Dictionary<string, string> { ["TP001"] = "Truck" },
        LoadingPoint: new Dictionary<string, string> { ["LP001"] = "Turkey" },
        LogisticsCompany: new Dictionary<string, string> { ["C001"] = "AGS" },
        Broker: new Dictionary<string, string> { ["BC001"] = "KLG" });

    private static InvoiceListEntryDto Header(
        string exportNumber = "26GE001",
        string? exportType = "ET003",
        string? exportTypeDesc = "Source Export Text") => new(
        InvoiceNumber: "INV-001",
        InvoiceDate: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        ExportNumber: exportNumber,
        ERPTransferWarehouseCode: null,
        ERPTransferWarehouseDescription: null,
        Arrival_Customs: "AC001",
        Arrival_Customs_Desc: "Source Moscow Text",
        Export_Type: exportType,
        Export_Type_Desc: exportTypeDesc,
        Transport: "TP001",
        Transport_Desc: "Source Truck Text",
        Loading_Point: "LP001",
        Loading_Point_Desc: "Source Turkey Text",
        Carier: "C001",
        Carier_Desc: "Source AGS Text",
        Broker_Company: "BC001",
        Broker_Company_Desc: "Source KLG Text",
        Status: 0,
        eInvoiceNumber: null);

    private static RawShipmentFeedDto Raw(InvoiceListEntryDto? header = null, IReadOnlyList<InvoiceDetailLineDto>? lines = null) =>
        new(header ?? Header(), lines ?? []);

    [Fact]
    public void Known_codes_resolve_to_their_description()
    {
        var fields = ShipmentStandardizer.Standardize(Raw(), Lookups());

        fields.ArrivalCustoms.Should().Be("Moscow");
        fields.ExportType.Should().Be("Export");
        fields.TransportType.Should().Be("Truck");
        fields.LoadingPoint.Should().Be("Turkey");
        fields.LogisticsCompany.Should().Be("AGS");
        fields.BrokerCompany.Should().Be("KLG");
        fields.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Unknown_code_falls_back_to_the_sources_own_description_with_one_warning()
    {
        var fields = ShipmentStandardizer.Standardize(Raw(Header(exportType: "ET999", exportTypeDesc: "Source Text")), Lookups());

        fields.ExportType.Should().Be("Source Text");
        fields.Warnings.Should().ContainSingle(w => w.Contains("ET999"));
    }

    [Fact]
    public void Unknown_code_with_no_source_description_falls_back_to_the_raw_code()
    {
        var fields = ShipmentStandardizer.Standardize(Raw(Header(exportType: "ET999", exportTypeDesc: null)), Lookups());

        fields.ExportType.Should().Be("ET999");
    }

    [Fact]
    public void Blank_code_maps_to_null_with_no_warning()
    {
        var fields = ShipmentStandardizer.Standardize(Raw(Header(exportType: null)), Lookups());

        fields.ExportType.Should().BeNull();
        fields.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Blank_export_number_throws()
    {
        var act = () => ShipmentStandardizer.Standardize(Raw(Header(exportNumber: "")), Lookups());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Detail_lines_are_grouped_into_transfers_and_summed_into_shipment_totals()
    {
        InvoiceDetailLineDto Line(string package, string store, decimal qty) => new(
            "INV-001", DateTimeOffset.UtcNow, null, package, "OPT1", "BAR1", "M", qty, 10m, store, "EUR", "26GE001", null, null);

        var lines = new[] { Line("PKG1", "ST01", 3), Line("PKG2", "ST01", 5), Line("PKG3", "ST02", 7) };

        var fields = ShipmentStandardizer.Standardize(Raw(lines: lines), Lookups());

        fields.TotalTransfers.Should().Be(2);
        fields.TotalBoxes.Should().Be(3);
        fields.TotalItems.Should().Be(15);
    }
}

public class ShipmentFeedDefaultsTests
{
    [Fact]
    public void New_shipment_defaults_match_the_shared_status_display_convention()
    {
        var (status, currentStatus, performance) = ShipmentFeedDefaults.ForNewShipment();

        status.Should().Be(TrackingStatus.Created);
        currentStatus.Should().Be(TrackingStatus.Created.ToDisplay());
        performance.Should().Be(PerformanceStatus.NotStarted.ToDisplay());
    }

    [Fact]
    public void New_transfer_inherits_the_shipments_seed_status()
    {
        var (currentStatus, performance) = ShipmentFeedDefaults.ForNewTransfer(TrackingStatus.AtCrossdock);

        currentStatus.Should().Be(TrackingStatus.AtCrossdock.ToDisplay());
        performance.Should().Be(PerformanceStatus.NotStarted.ToDisplay());
    }
}
