using FluentAssertions;
using LTS.Domain.Entities;
using LTS.Domain.Enums;
using LTS.Domain.Services;

namespace LTS.Tests.Domain;

public class TrackingStatusCalculatorTests
{
    private static Shipment NewShipment() => new()
    {
        ReferenceNo = "REF-001",
        InvoiceNo = "INV-001",
        ArrivalCountryId = 1
    };

    private static Transfer NewTransfer(Shipment shipment) => new()
    {
        ShipmentId = shipment.Id,
        Shipment = shipment,
        StoreId = 1,
        TransferNo = Transfer.BuildTransferNo(shipment.ReferenceNo, "S100")
    };

    [Fact]
    public void A_shipment_with_no_dates_is_created()
    {
        var (status, date) = TrackingStatusCalculator.ForShipment(NewShipment());

        status.Should().Be(TrackingStatus.Created);
        date.Should().BeNull();
    }

    [Fact]
    public void Status_is_the_furthest_milestone_reached_with_its_date()
    {
        var shipment = NewShipment();
        shipment.LoadingDate = new DateOnly(2026, 3, 1);
        shipment.DepartureDate = new DateOnly(2026, 3, 3);

        var (status, date) = TrackingStatusCalculator.ForShipment(shipment);

        status.Should().Be(TrackingStatus.Departed);
        date.Should().Be(new DateOnly(2026, 3, 3));
    }

    [Fact]
    public void A_gap_in_the_middle_does_not_hold_the_status_back()
    {
        // Export clearance was never entered, but the shipment has demonstrably arrived.
        var shipment = NewShipment();
        shipment.LoadingDate = new DateOnly(2026, 3, 1);
        shipment.ArrivalToTargetCountryDate = new DateOnly(2026, 3, 6);

        TrackingStatusCalculator.ForShipment(shipment).Status
            .Should().Be(TrackingStatus.ArrivedInTargetCountry);
    }

    [Fact]
    public void Shipment_status_stops_at_the_crossdock()
    {
        var shipment = NewShipment();
        shipment.CrossdockArrivalDate = new DateOnly(2026, 3, 8);

        TrackingStatusCalculator.ForShipment(shipment).Status.Should().Be(TrackingStatus.AtCrossdock);
    }

    [Fact]
    public void A_transfer_without_its_own_dates_shows_the_shipments_status()
    {
        var shipment = NewShipment();
        shipment.LoadingDate = new DateOnly(2026, 3, 1);
        shipment.CustomsStartDate = new DateOnly(2026, 3, 6);

        var (status, date) = TrackingStatusCalculator.ForTransfer(NewTransfer(shipment));

        status.Should().Be(TrackingStatus.CustomsInProgress);
        date.Should().Be(new DateOnly(2026, 3, 6));
    }

    [Fact]
    public void A_transfer_advances_past_its_shipment_once_it_leaves_the_crossdock()
    {
        var shipment = NewShipment();
        shipment.CrossdockArrivalDate = new DateOnly(2026, 3, 8);

        var transfer = NewTransfer(shipment);
        transfer.CrossdockDepartureDate = new DateOnly(2026, 3, 9);
        transfer.StoreArrivalDate = new DateOnly(2026, 3, 10);

        var (status, date) = TrackingStatusCalculator.ForTransfer(transfer);

        status.Should().Be(TrackingStatus.ArrivedAtStore);
        date.Should().Be(new DateOnly(2026, 3, 10));
    }

    [Fact]
    public void A_planned_store_arrival_is_a_forecast_and_never_advances_the_status()
    {
        var shipment = NewShipment();
        shipment.CrossdockArrivalDate = new DateOnly(2026, 3, 8);

        var transfer = NewTransfer(shipment);
        transfer.PlannedStoreArrivalDate = new DateOnly(2026, 3, 11);

        TrackingStatusCalculator.ForTransfer(transfer).Status.Should().Be(TrackingStatus.AtCrossdock);
    }

    [Fact]
    public void Acceptance_is_the_end_of_the_line()
    {
        var shipment = NewShipment();
        shipment.CrossdockArrivalDate = new DateOnly(2026, 3, 8);

        var transfer = NewTransfer(shipment);
        transfer.CrossdockDepartureDate = new DateOnly(2026, 3, 9);
        transfer.StoreArrivalDate = new DateOnly(2026, 3, 10);
        transfer.StorePreAcceptanceDate = new DateOnly(2026, 3, 11);
        transfer.StoreAcceptanceDate = new DateOnly(2026, 3, 12);

        TrackingStatusCalculator.ForTransfer(transfer).Status.Should().Be(TrackingStatus.Accepted);
    }

    [Fact]
    public void Transfer_evaluation_dates_include_the_parent_shipments_dates()
    {
        var shipment = NewShipment();
        shipment.CrossdockArrivalDate = new DateOnly(2026, 3, 8);

        var transfer = NewTransfer(shipment);
        transfer.CrossdockDepartureDate = new DateOnly(2026, 3, 9);

        var dates = transfer.GetMilestoneDates();

        dates[MilestoneType.CrossdockArrival].Should().Be(new DateOnly(2026, 3, 8));
        dates[MilestoneType.CrossdockDeparture].Should().Be(new DateOnly(2026, 3, 9));
    }

    [Fact]
    public void Transfer_number_is_reference_and_store_code()
    {
        Transfer.BuildTransferNo("REF-001", "S100").Should().Be("REF-001_S100");
    }
}
