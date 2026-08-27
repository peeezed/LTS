using FluentAssertions;
using LTS.Application.ExportAttributeFeed;
using LTS.Application.ShipmentFeed;

namespace LTS.Tests.ExportAttributeFeed;

public class ExportAttributeStandardizerTests
{
    private static AttributeCodeLookups Lookups() => new(
        ArrivalCustoms: new Dictionary<string, string> { ["AC001"] = "Moscow" },
        ExportType: new Dictionary<string, string> { ["ET003"] = "Export" },
        TransportType: new Dictionary<string, string> { ["TP002"] = "Air" },
        LoadingPoint: new Dictionary<string, string> { ["LP001"] = "Turkey" },
        LogisticsCompany: new Dictionary<string, string> { ["C001"] = "AGS" },
        Broker: new Dictionary<string, string> { ["BC001"] = "TLK" });

    private static ExportFileDetailDto Detail(
        string exportFileNumber = "26RUA377",
        string? exportType = "ET003",
        string? exportTypeDesc = "Source Export Text") => new(
        ExportFileNumber: exportFileNumber,
        ArrivalCustoms: "AC001",
        ArrivalCustomsDesc: "Source Moscow Text",
        ExportType: exportType,
        ExportTypeDesc: exportTypeDesc,
        Transport: "TP002",
        TransportDesc: "Source Air Text",
        LoadingPoint: "LP001",
        LoadingPointDesc: "Source Turkey Text",
        Carier: "C001",
        CarierDesc: "Source AGS Text",
        BrokerCompany: "BC001",
        BrokerCompanyDesc: "Source TLK Text");

    [Fact]
    public void Known_codes_resolve_to_their_description()
    {
        var fields = ExportAttributeStandardizer.Standardize(Detail(), Lookups());

        fields.ReferenceNo.Should().Be("26RUA377");
        fields.ArrivalCustoms.Should().Be("Moscow");
        fields.ExportType.Should().Be("Export");
        fields.TransportType.Should().Be("Air");
        fields.LoadingPoint.Should().Be("Turkey");
        fields.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Carier_resolves_against_the_logistics_company_lookup_table()
    {
        var fields = ExportAttributeStandardizer.Standardize(Detail(), Lookups());

        fields.LogisticsCompany.Should().Be("AGS");
    }

    [Fact]
    public void BrokerCompany_resolves_against_the_broker_lookup_table()
    {
        var fields = ExportAttributeStandardizer.Standardize(Detail(), Lookups());

        fields.BrokerCompany.Should().Be("TLK");
    }

    [Fact]
    public void Unknown_code_falls_back_to_the_sources_own_description_with_one_warning()
    {
        var fields = ExportAttributeStandardizer.Standardize(Detail(exportType: "ET999", exportTypeDesc: "Source Text"), Lookups());

        fields.ExportType.Should().Be("Source Text");
        fields.Warnings.Should().ContainSingle(w => w.Contains("ET999"));
    }

    [Fact]
    public void Unknown_code_with_no_source_description_falls_back_to_the_raw_code()
    {
        var fields = ExportAttributeStandardizer.Standardize(Detail(exportType: "ET999", exportTypeDesc: null), Lookups());

        fields.ExportType.Should().Be("ET999");
    }

    [Fact]
    public void Blank_code_maps_to_null_with_no_warning()
    {
        var fields = ExportAttributeStandardizer.Standardize(Detail(exportType: null), Lookups());

        fields.ExportType.Should().BeNull();
        fields.Warnings.Should().BeEmpty();
    }
}
