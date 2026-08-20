using FluentAssertions;
using LTS.Application.ShipmentFeed;

namespace LTS.Tests.ShipmentFeed;

public class ShipmentFeedCombinerTests
{
    private static readonly ShipmentReferenceDto Reference = new() { ReferenceNo = "26GE001" };

    private static readonly ShipmentHeaderDetailDto Header = new()
    {
        ReferenceNo = "26GE001",
        InvoiceNo = "INV-001",
        InvoiceDate = new DateOnly(2026, 8, 1)
    };

    private static readonly ShipmentAttributesDetailDto Attributes = new()
    {
        ArrivalCustoms = "AC001",
        ExportType = "ET003",
        TransportType = "TP001",
        LoadingPoint = "LP001",
        LogisticsCompany = "C001",
        BrokerCompany = "BC001"
    };

    private static readonly ShipmentCountsDetailDto Counts = new()
    {
        TotalTransfers = 2,
        TotalBoxes = 10,
        TotalItems = 100
    };

    [Fact]
    public void All_pieces_present_land_every_field_in_the_combined_record()
    {
        var combined = ShipmentFeedCombiner.Combine(Reference, Header, Attributes, Counts);

        combined.ReferenceNo.Should().Be("26GE001");
        combined.InvoiceNo.Should().Be("INV-001");
        combined.InvoiceDate.Should().Be(new DateOnly(2026, 8, 1));
        combined.ArrivalCustoms.Should().Be("AC001");
        combined.ExportType.Should().Be("ET003");
        combined.TransportType.Should().Be("TP001");
        combined.LoadingPoint.Should().Be("LP001");
        combined.LogisticsCompany.Should().Be("C001");
        combined.BrokerCompany.Should().Be("BC001");
        combined.TotalTransfers.Should().Be(2);
        combined.TotalBoxes.Should().Be(10);
        combined.TotalItems.Should().Be(100);
    }

    [Fact]
    public void A_missing_detail_call_leaves_its_fields_blank_without_failing_the_merge()
    {
        var combined = ShipmentFeedCombiner.Combine(Reference, header: null, Attributes, Counts);

        combined.ReferenceNo.Should().Be("26GE001"); // falls back to the list entry's reference
        combined.InvoiceNo.Should().BeNull();
        combined.InvoiceDate.Should().BeNull();
        combined.ArrivalCustoms.Should().Be("AC001");
    }

    [Fact]
    public void Downstream_standardize_does_not_throw_when_only_the_reference_is_known()
    {
        var combined = ShipmentFeedCombiner.Combine(Reference, header: null, attributes: null, counts: null);

        var lookups = new AttributeCodeLookups(
            new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>(),
            new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>());

        var act = () => ShipmentStandardizer.Standardize(combined, lookups);

        act.Should().NotThrow();
    }
}
