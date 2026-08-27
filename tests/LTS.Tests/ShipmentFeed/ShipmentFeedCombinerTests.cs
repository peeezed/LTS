using FluentAssertions;
using LTS.Application.ShipmentFeed;

namespace LTS.Tests.ShipmentFeed;

public class ShipmentFeedCombinerTests
{
    private static InvoiceDetailLineDto Line(string packageNumber, string storeCode, decimal quantity) => new(
        InvoiceNumber: "INV-001",
        InvoiceDate: DateTimeOffset.UtcNow,
        ShippingNumber: null,
        PackageNumber: packageNumber,
        OptionCode: "OPT1",
        Barcode: "BAR1",
        SizeCode: "M",
        Quantity: quantity,
        Amount: 10m,
        StoreCode: storeCode,
        CurrencyCode: "EUR",
        ExportNumber: "26GE001",
        PackageDimension: null,
        TotalPackageWeight: null);

    [Fact]
    public void Multiple_lines_for_the_same_store_and_package_sum_into_one_box()
    {
        var lines = new[] { Line("PKG1", "ST01", 3), Line("PKG1", "ST01", 5), Line("PKG1", "ST01", 2) };

        var transfers = ShipmentFeedCombiner.GroupIntoTransfers("26GE001", lines);

        transfers.Should().ContainSingle();
        transfers[0].Boxes.Should().ContainSingle(b => b.PackageNo == "PKG1" && b.ProductCount == 10);
    }

    [Fact]
    public void Multiple_packages_under_one_store_produce_one_transfer_with_summed_totals()
    {
        var lines = new[] { Line("PKG1", "ST01", 3), Line("PKG2", "ST01", 5) };

        var transfers = ShipmentFeedCombiner.GroupIntoTransfers("26GE001", lines);

        transfers.Should().ContainSingle();
        transfers[0].TotalBoxes.Should().Be(2);
        transfers[0].TotalItems.Should().Be(8);
        transfers[0].TransferNo.Should().Be("26GE001_ST01");
        transfers[0].ReceivingStoreCode.Should().Be("ST01");
    }

    [Fact]
    public void Multiple_stores_produce_multiple_transfers()
    {
        var lines = new[] { Line("PKG1", "ST01", 3), Line("PKG2", "ST02", 5) };

        var transfers = ShipmentFeedCombiner.GroupIntoTransfers("26GE001", lines);

        transfers.Should().HaveCount(2);
        transfers.Select(t => t.TransferNo).Should().BeEquivalentTo("26GE001_ST01", "26GE001_ST02");
    }

    [Fact]
    public void Empty_detail_lines_produce_no_transfers()
    {
        var transfers = ShipmentFeedCombiner.GroupIntoTransfers("26GE001", []);

        transfers.Should().BeEmpty();
    }
}
