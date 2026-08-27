using FluentAssertions;
using LTS.Domain.Enums;
using LTS.Domain.Kpi;

namespace LTS.Tests.Kpi;

public class IntegrationKpiDisplayTests
{
    [Fact]
    public void Days_between_a_start_and_its_deadline_is_the_difference_in_days()
    {
        var days = IntegrationKpiDisplay.DaysBetween(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 4));

        days.Should().Be(3);
    }

    [Fact]
    public void No_start_date_yields_no_days()
    {
        var days = IntegrationKpiDisplay.DaysBetween(null, new DateOnly(2026, 3, 4));

        days.Should().BeNull();
    }

    [Fact]
    public void No_deadline_yields_no_days()
    {
        var days = IntegrationKpiDisplay.DaysBetween(new DateOnly(2026, 3, 1), null);

        days.Should().BeNull();
    }

    [Fact]
    public void An_actual_date_after_its_deadline_is_late()
    {
        var isLate = IntegrationKpiDisplay.IsLate(new DateOnly(2026, 3, 6), new DateOnly(2026, 3, 5));

        isLate.Should().BeTrue();
    }

    [Fact]
    public void An_actual_date_on_its_deadline_is_not_late()
    {
        var isLate = IntegrationKpiDisplay.IsLate(new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 5));

        isLate.Should().BeFalse();
    }

    [Fact]
    public void An_actual_date_before_its_deadline_is_not_late()
    {
        var isLate = IntegrationKpiDisplay.IsLate(new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 5));

        isLate.Should().BeFalse();
    }

    [Fact]
    public void No_actual_date_is_not_late()
    {
        var isLate = IntegrationKpiDisplay.IsLate(null, new DateOnly(2026, 3, 5));

        isLate.Should().BeFalse();
    }

    [Fact]
    public void No_deadline_is_not_late()
    {
        var isLate = IntegrationKpiDisplay.IsLate(new DateOnly(2026, 3, 6), null);

        isLate.Should().BeFalse();
    }
}

public class IntegrationKpiResolverTests
{
    private static readonly KpiAttributeScope Shipment = new("Export", "Istanbul", "Georgia Customs", "Truck");

    private static IntegrationKpiTargetSnapshot Target(
        int countryId, IntegrationKpiStep step,
        string? exportType = null, string? loadingPoint = null, string? arrivalCustoms = null, string? transportType = null,
        int targetDays = 5, bool isActive = true) =>
        new(countryId, step, new KpiAttributeScope(exportType, loadingPoint, arrivalCustoms, transportType), targetDays, isActive);

    [Fact]
    public void A_target_for_a_different_country_never_matches()
    {
        var targets = new[] { Target(countryId: 2, IntegrationKpiStep.LoadingToCustomsClearance, targetDays: 10) };

        var result = IntegrationKpiResolver.ResolveTargetDays(
            IntegrationKpiStep.LoadingToCustomsClearance, countryId: 1, Shipment, targets);

        result.Should().BeNull();
    }

    [Fact]
    public void A_fully_wildcarded_target_matches_any_shipment_in_the_same_country()
    {
        var targets = new[] { Target(countryId: 1, IntegrationKpiStep.LoadingToCustomsClearance, targetDays: 10) };

        var result = IntegrationKpiResolver.ResolveTargetDays(
            IntegrationKpiStep.LoadingToCustomsClearance, countryId: 1, Shipment, targets);

        result.Should().Be(10);
    }

    [Fact]
    public void The_most_specific_matching_target_wins()
    {
        var targets = new[]
        {
            Target(countryId: 1, IntegrationKpiStep.LoadingToCustomsClearance, targetDays: 10),
            Target(countryId: 1, IntegrationKpiStep.LoadingToCustomsClearance, exportType: "Export", targetDays: 5),
            Target(countryId: 1, IntegrationKpiStep.LoadingToCustomsClearance,
                exportType: "Export", loadingPoint: "Istanbul", targetDays: 3)
        };

        var result = IntegrationKpiResolver.ResolveTargetDays(
            IntegrationKpiStep.LoadingToCustomsClearance, countryId: 1, Shipment, targets);

        result.Should().Be(3);
    }

    [Fact]
    public void A_target_scoped_to_a_different_value_does_not_match()
    {
        var targets = new[]
        {
            Target(countryId: 1, IntegrationKpiStep.LoadingToCustomsClearance, exportType: "Other", targetDays: 10)
        };

        var result = IntegrationKpiResolver.ResolveTargetDays(
            IntegrationKpiStep.LoadingToCustomsClearance, countryId: 1, Shipment, targets);

        result.Should().BeNull();
    }

    [Fact]
    public void An_inactive_target_never_matches()
    {
        var targets = new[]
        {
            Target(countryId: 1, IntegrationKpiStep.LoadingToCustomsClearance, targetDays: 10, isActive: false)
        };

        var result = IntegrationKpiResolver.ResolveTargetDays(
            IntegrationKpiStep.LoadingToCustomsClearance, countryId: 1, Shipment, targets);

        result.Should().BeNull();
    }

    [Fact]
    public void No_matching_target_returns_null()
    {
        var result = IntegrationKpiResolver.ResolveTargetDays(
            IntegrationKpiStep.LoadingToCustomsClearance, countryId: 1, Shipment, []);

        result.Should().BeNull();
    }
}

public class IntegrationKpiEvaluatorTests
{
    private static readonly DateOnly Today = new(2026, 3, 20);
    private static readonly KpiAttributeScope CompleteScope = new("Export", "Istanbul", "Georgia Customs", "Truck");

    [Fact]
    public void A_leg_with_no_start_date_is_not_started()
    {
        var status = IntegrationKpiEvaluator.EvaluateLeg(new KpiLegDates(null, null, null), Today);

        status.Should().Be(PerformanceStatus.NotStarted);
    }

    [Fact]
    public void A_started_leg_with_no_resolved_deadline_has_no_target()
    {
        var status = IntegrationKpiEvaluator.EvaluateLeg(
            new KpiLegDates(new DateOnly(2026, 3, 1), null, null), Today);

        status.Should().Be(PerformanceStatus.NoTarget);
    }

    [Fact]
    public void A_completed_leg_within_its_deadline_is_on_time()
    {
        var status = IntegrationKpiEvaluator.EvaluateLeg(
            new KpiLegDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 5)), Today);

        status.Should().Be(PerformanceStatus.OnTime);
    }

    [Fact]
    public void A_completed_leg_exactly_on_its_deadline_is_on_time()
    {
        var status = IntegrationKpiEvaluator.EvaluateLeg(
            new KpiLegDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 5)), Today);

        status.Should().Be(PerformanceStatus.OnTime);
    }

    [Fact]
    public void A_completed_leg_past_its_deadline_is_late()
    {
        var status = IntegrationKpiEvaluator.EvaluateLeg(
            new KpiLegDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 6), new DateOnly(2026, 3, 5)), Today);

        status.Should().Be(PerformanceStatus.Late);
    }

    [Fact]
    public void A_running_leg_still_inside_its_deadline_is_on_track()
    {
        var status = IntegrationKpiEvaluator.EvaluateLeg(
            new KpiLegDates(new DateOnly(2026, 3, 1), null, new DateOnly(2026, 3, 25)), Today);

        status.Should().Be(PerformanceStatus.OnTrack);
    }

    [Fact]
    public void A_running_leg_past_its_deadline_is_overdue()
    {
        var status = IntegrationKpiEvaluator.EvaluateLeg(
            new KpiLegDates(new DateOnly(2026, 3, 1), null, new DateOnly(2026, 3, 15)), Today);

        status.Should().Be(PerformanceStatus.Overdue);
    }

    [Fact]
    public void A_shipment_missing_any_required_attribute_short_circuits_to_missing_attributes()
    {
        var incomplete = CompleteScope with { TransportType = null };
        var legs = new[] { new KpiLegDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 10)) };

        var status = IntegrationKpiEvaluator.EvaluateShipment(incomplete, legs, [], Today);

        status.Should().Be(PerformanceStatus.MissingAttributes);
    }

    [Fact]
    public void Missing_attributes_short_circuits_even_when_a_target_would_otherwise_have_matched()
    {
        var incomplete = CompleteScope with { ExportType = null };
        var onTimeLeg = new[] { new KpiLegDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 10)) };

        var status = IntegrationKpiEvaluator.EvaluateShipment(incomplete, onTimeLeg, [], Today);

        status.Should().Be(PerformanceStatus.MissingAttributes);
    }

    [Fact]
    public void Shipment_level_performance_is_the_worst_across_every_leg()
    {
        var legs = new[]
        {
            new KpiLegDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 10)), // OnTime
            new KpiLegDates(new DateOnly(2026, 3, 1), null, new DateOnly(2026, 3, 15)) // Overdue
        };

        var status = IntegrationKpiEvaluator.EvaluateShipment(CompleteScope, legs, [], Today);

        status.Should().Be(PerformanceStatus.Overdue);
    }

    [Fact]
    public void Xdock_legs_contribute_to_the_shipment_level_rollup_per_transfer()
    {
        var shipmentLegs = new[]
        {
            new KpiLegDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 10)) // OnTime
        };

        var xdockLegs = new[]
        {
            new KpiLegDates(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 11), new DateOnly(2026, 3, 12)), // OnTime
            new KpiLegDates(new DateOnly(2026, 3, 10), null, new DateOnly(2026, 3, 15)) // Overdue, a different transfer
        };

        var status = IntegrationKpiEvaluator.EvaluateShipment(CompleteScope, shipmentLegs, xdockLegs, Today);

        status.Should().Be(PerformanceStatus.Overdue);
    }

    [Fact]
    public void No_delayed_leg_returns_null()
    {
        var legs = new[]
        {
            (IntegrationKpiStep.LoadingToCustomsClearance,
                new KpiLegDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 10))), // OnTime
            (IntegrationKpiStep.CustomsToDeparture, new KpiLegDates(null, null, null)) // NotStarted
        };

        var delayed = IntegrationKpiEvaluator.FindDelayedLeg(legs, Today);

        delayed.Should().BeNull();
    }

    [Fact]
    public void A_late_leg_is_found()
    {
        var legs = new[]
        {
            (IntegrationKpiStep.LoadingToCustomsClearance,
                new KpiLegDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 6), new DateOnly(2026, 3, 5))) // Late
        };

        var delayed = IntegrationKpiEvaluator.FindDelayedLeg(legs, Today);

        delayed.Should().NotBeNull();
        delayed!.Value.Step.Should().Be(IntegrationKpiStep.LoadingToCustomsClearance);
        delayed.Value.Status.Should().Be(PerformanceStatus.Late);
    }

    [Fact]
    public void An_overdue_leg_is_found()
    {
        var legs = new[]
        {
            (IntegrationKpiStep.Xdock, new KpiLegDates(new DateOnly(2026, 3, 1), null, new DateOnly(2026, 3, 15))) // Overdue
        };

        var delayed = IntegrationKpiEvaluator.FindDelayedLeg(legs, Today);

        delayed.Should().NotBeNull();
        delayed!.Value.Step.Should().Be(IntegrationKpiStep.Xdock);
        delayed.Value.Status.Should().Be(PerformanceStatus.Overdue);
    }

    [Fact]
    public void The_first_qualifying_leg_in_sequence_wins_when_more_than_one_is_delayed()
    {
        var legs = new[]
        {
            (IntegrationKpiStep.LoadingToCustomsClearance,
                new KpiLegDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 6), new DateOnly(2026, 3, 5))), // Late
            (IntegrationKpiStep.Xdock, new KpiLegDates(new DateOnly(2026, 3, 1), null, new DateOnly(2026, 3, 15))) // Overdue
        };

        var delayed = IntegrationKpiEvaluator.FindDelayedLeg(legs, Today);

        delayed!.Value.Step.Should().Be(IntegrationKpiStep.LoadingToCustomsClearance);
    }
}
