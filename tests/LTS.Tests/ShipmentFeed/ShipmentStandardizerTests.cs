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

    private static RawShipmentFeedDto Raw(
        string referenceNo = "26GE001",
        string? arrivalCustoms = "AC001",
        string? exportType = "ET003",
        string? transportType = "TP001",
        string? loadingPoint = "LP001",
        string? logisticsCompany = "C001",
        string? brokerCompany = "BC001") => new()
    {
        ReferenceNo = referenceNo,
        InvoiceNo = "INV-001",
        InvoiceDate = new DateOnly(2026, 8, 1),
        ArrivalCustoms = arrivalCustoms,
        ExportType = exportType,
        TransportType = transportType,
        LoadingPoint = loadingPoint,
        LogisticsCompany = logisticsCompany,
        BrokerCompany = brokerCompany,
        TotalTransfers = 2,
        TotalBoxes = 10,
        TotalItems = 100
    };

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
    public void Unknown_code_falls_back_to_the_raw_code_with_one_warning()
    {
        var fields = ShipmentStandardizer.Standardize(Raw(exportType: "ET999"), Lookups());

        fields.ExportType.Should().Be("ET999");
        fields.Warnings.Should().ContainSingle(w => w.Contains("ET999"));
    }

    [Fact]
    public void Blank_code_maps_to_null_with_no_warning()
    {
        var fields = ShipmentStandardizer.Standardize(Raw(exportType: null), Lookups());

        fields.ExportType.Should().BeNull();
        fields.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Invoice_and_count_fields_pass_through_unchanged()
    {
        var fields = ShipmentStandardizer.Standardize(Raw(), Lookups());

        fields.ReferenceNo.Should().Be("26GE001");
        fields.InvoiceNo.Should().Be("INV-001");
        fields.InvoiceDate.Should().Be(new DateOnly(2026, 8, 1));
        fields.TotalTransfers.Should().Be(2);
        fields.TotalBoxes.Should().Be(10);
        fields.TotalItems.Should().Be(100);
    }

    [Fact]
    public void Blank_reference_no_throws()
    {
        var raw = Raw(referenceNo: "") with { ReferenceNo = null };

        var act = () => ShipmentStandardizer.Standardize(raw, Lookups());

        act.Should().Throw<InvalidOperationException>();
    }
}

public class ShipmentFeedDefaultsTests
{
    [Fact]
    public void New_shipment_defaults_match_the_shared_status_display_convention()
    {
        var (status, performance) = ShipmentFeedDefaults.ForNewShipment();

        status.Should().Be(TrackingStatus.Created.ToDisplay());
        performance.Should().Be(PerformanceStatus.NotStarted.ToDisplay());
    }
}
