using FluentAssertions;
using LTS.Application.Security;
using LTS.Application.Tracking;
using LTS.Domain.Entities;
using LTS.Domain.Enums;
using LTS.Domain.Kpi;
using LTS.Infrastructure.Persistence;
using LTS.Infrastructure.Tracking;
using Microsoft.EntityFrameworkCore;

namespace LTS.Tests.Tracking;

/// <summary>
/// Every date in LTS is written through this service, so its rules — who may write, what may be
/// written, and what is recorded — are the ones worth pinning down.
/// </summary>
public class MilestoneServiceTests
{
    private static readonly FixedClock Clock = new(new DateOnly(2026, 3, 20));

    private static MilestoneService Build(LtsDbContext db, params KpiTarget[] targets) =>
        new(db, new StubKpiTargetProvider(targets), new TestCurrentUser(Guid.NewGuid(), "tester"), Clock);

    private static MilestoneChange Change(string reference, MilestoneType type, int year = 2026, int month = 3, int day = 10) =>
        new(reference, type, new DateOnly(year, month, day));

    [Fact]
    public async Task A_broker_can_write_its_own_customs_dates()
    {
        using var db = TestDb.Create();
        var service = Build(db);

        var result = await service.ApplyAsync(
            [Change("REF-A", MilestoneType.CustomsStart)],
            MilestoneApplyOptions.Manual,
            TestDb.Permissions(UserType.Broker, TestDb.AtlasBroker));

        result.Applied.Should().Be(1);
        result.Errors.Should().BeEmpty();

        var shipment = await db.Shipments.FirstAsync(s => s.ReferenceNo == "REF-A");
        shipment.CustomsStartDate.Should().Be(new DateOnly(2026, 3, 10));
    }

    [Fact]
    public async Task A_broker_cannot_write_a_carriers_date()
    {
        using var db = TestDb.Create();
        var service = Build(db);

        var result = await service.ApplyAsync(
            [Change("REF-A", MilestoneType.Loading)],
            MilestoneApplyOptions.Manual,
            TestDb.Permissions(UserType.Broker, TestDb.AtlasBroker));

        result.Applied.Should().Be(0);
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("not allowed");

        var shipment = await db.Shipments.FirstAsync(s => s.ReferenceNo == "REF-A");
        shipment.LoadingDate.Should().BeNull();
    }

    [Fact]
    public async Task A_broker_cannot_write_to_another_brokers_shipment()
    {
        using var db = TestDb.Create();
        var service = Build(db);

        var result = await service.ApplyAsync(
            [Change("REF-C", MilestoneType.CustomsStart)],
            MilestoneApplyOptions.Manual,
            TestDb.Permissions(UserType.Broker, TestDb.AtlasBroker));

        result.Applied.Should().Be(0);
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("do not have access");
    }

    [Fact]
    public async Task An_unknown_reference_is_reported_rather_than_silently_ignored()
    {
        using var db = TestDb.Create();
        var service = Build(db);

        var result = await service.ApplyAsync(
            [Change("REF-NOPE", MilestoneType.CustomsStart)],
            MilestoneApplyOptions.Manual,
            TestDb.Permissions(UserType.LogisticsDepartment));

        result.Applied.Should().Be(0);
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("No shipment found");
    }

    [Fact]
    public async Task A_shipment_can_be_found_by_its_invoice_number_too()
    {
        using var db = TestDb.Create();
        var service = Build(db);

        var result = await service.ApplyAsync(
            [Change("INV-REF-A", MilestoneType.CustomsStart)],
            MilestoneApplyOptions.Manual,
            TestDb.Permissions(UserType.LogisticsDepartment));

        result.Applied.Should().Be(1);
    }

    [Fact]
    public async Task A_date_that_lands_before_the_milestone_it_must_follow_is_rejected()
    {
        using var db = TestDb.Create();
        var service = Build(db);
        var permissions = TestDb.Permissions(UserType.LogisticsDepartment);

        await service.ApplyAsync([Change("REF-A", MilestoneType.Loading, day: 10)],
            MilestoneApplyOptions.Manual, permissions);

        // The classic data-entry slip: the right day, the wrong year.
        var result = await service.ApplyAsync(
            [Change("REF-A", MilestoneType.Departure, year: 2025)],
            MilestoneApplyOptions.Manual, permissions);

        result.Applied.Should().Be(0);
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("cannot be before");
    }

    [Fact]
    public async Task A_recorded_event_cannot_be_dated_in_the_future()
    {
        using var db = TestDb.Create();
        var service = Build(db);

        var result = await service.ApplyAsync(
            [new MilestoneChange("REF-A", MilestoneType.Loading, Clock.Today.AddDays(10))],
            MilestoneApplyOptions.Manual,
            TestDb.Permissions(UserType.LogisticsDepartment));

        result.Applied.Should().Be(0);
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("cannot be in the future");
    }

    [Fact]
    public async Task A_planned_store_arrival_is_allowed_to_be_in_the_future()
    {
        using var db = TestDb.Create();
        var service = Build(db);

        var result = await service.ApplyAsync(
            [new MilestoneChange("REF-A_TR100", MilestoneType.PlannedStoreArrival, Clock.Today.AddDays(5))],
            MilestoneApplyOptions.Manual,
            TestDb.Permissions(UserType.LogisticsDepartment));

        result.Applied.Should().Be(1, "a planned date is a forecast, not a record of something that happened");
    }

    [Fact]
    public async Task Writing_a_date_recomputes_the_status_and_the_transfers_below_it()
    {
        using var db = TestDb.Create();
        var service = Build(db);
        var permissions = TestDb.Permissions(UserType.LogisticsDepartment);

        await service.ApplyAsync(
            [Change("REF-A", MilestoneType.CrossdockArrival, day: 12)],
            MilestoneApplyOptions.Manual, permissions);

        var shipment = await db.Shipments.Include(s => s.Transfers).FirstAsync(s => s.ReferenceNo == "REF-A");

        shipment.CurrentStatus.Should().Be(TrackingStatus.AtCrossdock);
        shipment.CurrentStatusDate.Should().Be(new DateOnly(2026, 3, 12));
        shipment.Transfers.Single().CurrentStatus.Should().Be(TrackingStatus.AtCrossdock,
            "a transfer with no dates of its own inherits its shipment's status");
    }

    [Fact]
    public async Task Performance_is_scored_against_the_matching_target()
    {
        using var db = TestDb.Create();

        // Two days allowed for customs; the shipment takes five.
        var target = new KpiTarget
        {
            Step = KpiStep.CustomsStartToCustomsEnd,
            TargetDays = 2,
            EffectiveFrom = new DateOnly(2020, 1, 1)
        };

        var service = Build(db, target);
        var permissions = TestDb.Permissions(UserType.LogisticsDepartment);

        await service.ApplyAsync(
        [
            Change("REF-A", MilestoneType.CustomsStart, day: 10),
            Change("REF-A", MilestoneType.CustomsEnd, day: 15)
        ], MilestoneApplyOptions.Manual, permissions);

        var shipment = await db.Shipments.FirstAsync(s => s.ReferenceNo == "REF-A");
        shipment.Performance.Should().Be(PerformanceStatus.Late);
    }

    [Fact]
    public async Task Every_change_is_recorded_with_its_old_value_and_its_source()
    {
        using var db = TestDb.Create();
        var service = Build(db);
        var permissions = TestDb.Permissions(UserType.LogisticsDepartment);

        await service.ApplyAsync([Change("REF-A", MilestoneType.CustomsStart, day: 10)],
            MilestoneApplyOptions.Manual, permissions);
        await service.ApplyAsync([Change("REF-A", MilestoneType.CustomsStart, day: 11)],
            MilestoneApplyOptions.Manual, permissions);

        var audits = await db.MilestoneAudits
            .Where(a => a.MilestoneType == MilestoneType.CustomsStart)
            .OrderBy(a => a.Id)
            .ToListAsync();

        audits.Should().HaveCount(2);
        audits[0].OldValue.Should().BeNull();
        audits[0].NewValue.Should().Be(new DateOnly(2026, 3, 10));
        audits[1].OldValue.Should().Be(new DateOnly(2026, 3, 10), "the replaced value has to survive somewhere");
        audits[1].NewValue.Should().Be(new DateOnly(2026, 3, 11));
        audits[1].Source.Should().Be(MilestoneSource.Manual);
        audits[1].UserName.Should().Be("tester");
    }

    [Fact]
    public async Task Rewriting_the_same_value_is_counted_as_unchanged_and_not_audited()
    {
        using var db = TestDb.Create();
        var service = Build(db);
        var permissions = TestDb.Permissions(UserType.LogisticsDepartment);

        await service.ApplyAsync([Change("REF-A", MilestoneType.CustomsStart)],
            MilestoneApplyOptions.Manual, permissions);

        var result = await service.ApplyAsync([Change("REF-A", MilestoneType.CustomsStart)],
            MilestoneApplyOptions.Manual, permissions);

        result.Applied.Should().Be(0);
        result.Unchanged.Should().Be(1);
        (await db.MilestoneAudits.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Integration_writes_bypass_permissions_because_there_is_no_user_behind_them()
    {
        using var db = TestDb.Create();
        var service = Build(db);

        var result = await service.ApplyAsync(
            [Change("REF-A", MilestoneType.CrossdockArrival)],
            new MilestoneApplyOptions(MilestoneSource.Integration, EnforcePermissions: false),
            UserPermissions.None);

        result.Applied.Should().Be(1);
    }

    [Fact]
    public async Task An_integration_overwrites_a_manual_value_but_keeps_it_in_the_audit()
    {
        using var db = TestDb.Create();
        var service = Build(db);

        await service.ApplyAsync([Change("REF-A", MilestoneType.CrossdockArrival, day: 10)],
            MilestoneApplyOptions.Manual, TestDb.Permissions(UserType.LogisticsDepartment));

        await service.ApplyAsync([Change("REF-A", MilestoneType.CrossdockArrival, day: 12)],
            new MilestoneApplyOptions(MilestoneSource.Integration, EnforcePermissions: false),
            UserPermissions.None);

        var shipment = await db.Shipments.FirstAsync(s => s.ReferenceNo == "REF-A");
        shipment.CrossdockArrivalDate.Should().Be(new DateOnly(2026, 3, 12));

        var audits = await db.MilestoneAudits.OrderBy(a => a.Id).ToListAsync();
        audits.Last().OldValue.Should().Be(new DateOnly(2026, 3, 10));
        audits.Last().Source.Should().Be(MilestoneSource.Integration);
    }

    [Fact]
    public async Task A_source_set_to_defer_to_people_leaves_the_manual_value_alone()
    {
        using var db = TestDb.Create();
        var service = Build(db);

        await service.ApplyAsync([Change("REF-A", MilestoneType.CrossdockArrival, day: 10)],
            MilestoneApplyOptions.Manual, TestDb.Permissions(UserType.LogisticsDepartment));

        var result = await service.ApplyAsync([Change("REF-A", MilestoneType.CrossdockArrival, day: 12)],
            new MilestoneApplyOptions(MilestoneSource.Integration, EnforcePermissions: false, ManualOverrideWins: true),
            UserPermissions.None);

        result.Applied.Should().Be(0);

        var shipment = await db.Shipments.FirstAsync(s => s.ReferenceNo == "REF-A");
        shipment.CrossdockArrivalDate.Should().Be(new DateOnly(2026, 3, 10));

        // The disagreement is still visible even though the value did not change.
        var audits = await db.MilestoneAudits.OrderBy(a => a.Id).ToListAsync();
        audits.Last().Note.Should().Contain("kept the manually entered value");
    }

    [Fact]
    public async Task A_transfer_date_is_checked_against_the_shipments_dates_not_just_its_own()
    {
        using var db = TestDb.Create();
        var service = Build(db);
        var permissions = TestDb.Permissions(UserType.LogisticsDepartment);

        await service.ApplyAsync([Change("REF-A", MilestoneType.CrossdockArrival, day: 12)],
            MilestoneApplyOptions.Manual, permissions);

        var result = await service.ApplyAsync(
            [Change("REF-A_TR100", MilestoneType.CrossdockDeparture, day: 8)],
            MilestoneApplyOptions.Manual, permissions);

        result.Applied.Should().Be(0);
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("Crossdock Arrival");
    }

    [Fact]
    public async Task One_bad_row_does_not_stop_the_good_ones_in_the_same_batch()
    {
        using var db = TestDb.Create();
        var service = Build(db);

        var result = await service.ApplyAsync(
        [
            Change("REF-A", MilestoneType.CustomsStart),
            Change("REF-NOPE", MilestoneType.CustomsStart),
            Change("REF-B", MilestoneType.CustomsStart)
        ], MilestoneApplyOptions.Manual, TestDb.Permissions(UserType.Broker, TestDb.AtlasBroker));

        result.Applied.Should().Be(2);
        result.Errors.Should().ContainSingle();
    }

    [Fact]
    public async Task Clearing_a_date_is_allowed_and_rolls_the_status_back()
    {
        using var db = TestDb.Create();
        var service = Build(db);
        var permissions = TestDb.Permissions(UserType.LogisticsDepartment);

        await service.ApplyAsync([Change("REF-A", MilestoneType.CrossdockArrival, day: 12)],
            MilestoneApplyOptions.Manual, permissions);

        await service.ApplyAsync([new MilestoneChange("REF-A", MilestoneType.CrossdockArrival, null)],
            MilestoneApplyOptions.Manual, permissions);

        var shipment = await db.Shipments.FirstAsync(s => s.ReferenceNo == "REF-A");
        shipment.CrossdockArrivalDate.Should().BeNull();
        shipment.CurrentStatus.Should().Be(TrackingStatus.Created);
    }
}
