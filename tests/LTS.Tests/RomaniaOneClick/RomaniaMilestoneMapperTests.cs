using FluentAssertions;
using LTS.Application.RomaniaOneClick;
using LTS.Application.Tracking;
using LTS.Domain.Enums;

namespace LTS.Tests.RomaniaOneClick;

public class RomaniaMilestoneMapperTests
{
    private static RomaniaDomesticShipmentDto Shipment(
        DateOnly? shipmentDate = null,
        DateOnly? loadingActStartDate = null,
        DateOnly? unloadingStartDate = null,
        DateOnly? unloadingActStartDate = null) => new(
        PermShipmentId: "1234567",
        ShipmentStatus: "In transit",
        ShipmentDate: shipmentDate,
        LoadingActStartDate: loadingActStartDate,
        UnloadingStartDate: unloadingStartDate,
        UnloadingActStartDate: unloadingActStartDate);

    [Fact]
    public void Maps_all_three_transfer_scope_dates_when_present()
    {
        var shipment = Shipment(
            shipmentDate: new DateOnly(2026, 3, 1),
            loadingActStartDate: new DateOnly(2026, 3, 2),
            unloadingStartDate: new DateOnly(2026, 3, 4),
            unloadingActStartDate: new DateOnly(2026, 3, 5));

        var changes = RomaniaMilestoneMapper.BuildMilestoneChanges("T-1", shipment);

        changes.Should().BeEquivalentTo(new[]
        {
            new MilestoneChange("T-1", MilestoneType.CrossdockDeparture, new DateOnly(2026, 3, 2)),
            new MilestoneChange("T-1", MilestoneType.PlannedStoreArrival, new DateOnly(2026, 3, 4)),
            new MilestoneChange("T-1", MilestoneType.StoreArrival, new DateOnly(2026, 3, 5))
        });
    }

    [Fact]
    public void Never_maps_shipment_date_to_crossdock_arrival()
    {
        var shipment = Shipment(shipmentDate: new DateOnly(2026, 3, 1));

        var changes = RomaniaMilestoneMapper.BuildMilestoneChanges("T-1", shipment);

        changes.Should().NotContain(c => c.Type == MilestoneType.CrossdockArrival);
        changes.Should().BeEmpty();
    }

    [Fact]
    public void Omits_a_change_for_each_date_klg_has_not_reported_yet()
    {
        var shipment = Shipment(loadingActStartDate: new DateOnly(2026, 3, 2));

        var changes = RomaniaMilestoneMapper.BuildMilestoneChanges("T-1", shipment);

        changes.Should().ContainSingle()
            .Which.Should().Be(new MilestoneChange("T-1", MilestoneType.CrossdockDeparture, new DateOnly(2026, 3, 2)));
    }

    [Fact]
    public void No_dates_at_all_yields_no_changes()
    {
        var changes = RomaniaMilestoneMapper.BuildMilestoneChanges("T-1", Shipment());

        changes.Should().BeEmpty();
    }
}
