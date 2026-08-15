using FluentAssertions;
using LTS.Domain.Enums;
using LTS.Domain.Kpi;

namespace LTS.Tests.Kpi;

public class KpiEvaluatorTests
{
    private static readonly DateOnly Today = new(2026, 3, 20);
    private readonly KpiEvaluator _evaluator = new();

    private static KpiStepDefinition CustomsStep =>
        KpiStepCatalog.Get(KpiStep.CustomsStartToCustomsEnd);

    private KpiEvaluation EvaluateCustoms(DateOnly? start, DateOnly? end, int? target) =>
        _evaluator.Evaluate(
            new Dictionary<MilestoneType, DateOnly?>
            {
                [MilestoneType.CustomsStart] = start,
                [MilestoneType.CustomsEnd] = end
            },
            _ => target,
            Today,
            [CustomsStep]);

    [Fact]
    public void Step_with_no_start_date_is_not_started()
    {
        var result = EvaluateCustoms(start: null, end: null, target: 3);

        result.Steps.Single().Status.Should().Be(PerformanceStatus.NotStarted);
        result.Steps.Single().ActualDays.Should().BeNull();
    }

    [Fact]
    public void Completed_step_within_target_is_on_time()
    {
        var result = EvaluateCustoms(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 12), target: 3);

        var step = result.Steps.Single();
        step.Status.Should().Be(PerformanceStatus.OnTime);
        step.ActualDays.Should().Be(2);
        step.VarianceDays.Should().Be(-1);
    }

    [Fact]
    public void Completed_step_exactly_on_target_is_on_time()
    {
        var result = EvaluateCustoms(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 13), target: 3);

        result.Steps.Single().Status.Should().Be(PerformanceStatus.OnTime);
        result.Steps.Single().VarianceDays.Should().Be(0);
    }

    [Fact]
    public void Completed_step_past_target_is_late_with_positive_variance()
    {
        var result = EvaluateCustoms(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 15), target: 3);

        var step = result.Steps.Single();
        step.Status.Should().Be(PerformanceStatus.Late);
        step.VarianceDays.Should().Be(2);
    }

    [Fact]
    public void Running_step_well_inside_target_is_on_track()
    {
        // Started yesterday against a 10 day target: 10% consumed.
        var result = EvaluateCustoms(Today.AddDays(-1), end: null, target: 10);

        result.Steps.Single().Status.Should().Be(PerformanceStatus.OnTrack);
        result.Steps.Single().ActualDays.Should().Be(1);
    }

    [Fact]
    public void Running_step_past_the_at_risk_threshold_is_at_risk()
    {
        // 8 of 10 days consumed is exactly the 80% threshold.
        var result = EvaluateCustoms(Today.AddDays(-8), end: null, target: 10);

        result.Steps.Single().Status.Should().Be(PerformanceStatus.AtRisk);
    }

    [Fact]
    public void Running_step_past_target_is_overdue()
    {
        var result = EvaluateCustoms(Today.AddDays(-11), end: null, target: 10);

        result.Steps.Single().Status.Should().Be(PerformanceStatus.Overdue);
        result.Steps.Single().ActualDays.Should().Be(11);
    }

    [Fact]
    public void Same_day_target_is_at_risk_while_still_open()
    {
        var result = EvaluateCustoms(Today, end: null, target: 0);

        result.Steps.Single().Status.Should().Be(PerformanceStatus.AtRisk);
    }

    [Fact]
    public void Step_without_a_matching_target_still_reports_duration()
    {
        var result = EvaluateCustoms(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 15), target: null);

        var step = result.Steps.Single();
        step.Status.Should().Be(PerformanceStatus.NoTarget);
        step.ActualDays.Should().Be(5);
        step.VarianceDays.Should().BeNull();
    }

    [Fact]
    public void Future_start_date_is_clamped_to_zero_rather_than_reported_as_negative()
    {
        var result = EvaluateCustoms(Today.AddDays(5), end: null, target: 3);

        result.Steps.Single().ActualDays.Should().Be(0);
        result.Steps.Single().Status.Should().Be(PerformanceStatus.OnTrack);
    }

    [Fact]
    public void Overall_performance_is_the_worst_step()
    {
        var dates = new Dictionary<MilestoneType, DateOnly?>
        {
            [MilestoneType.Loading] = new DateOnly(2026, 3, 1),
            [MilestoneType.DepartureCustomsClearance] = new DateOnly(2026, 3, 2),   // on time
            [MilestoneType.Departure] = new DateOnly(2026, 3, 12),                  // late
            [MilestoneType.ArrivalToTargetCountry] = new DateOnly(2026, 3, 14)
        };

        var result = _evaluator.Evaluate(dates, _ => 3, Today, KpiStepCatalog.ShipmentSteps);

        result.Overall.Should().Be(PerformanceStatus.Overdue,
            "customs never started, so that step is running well past its target");
        result[KpiStep.LoadingToExportClearance]!.Status.Should().Be(PerformanceStatus.OnTime);
        result[KpiStep.ExportClearanceToDeparture]!.Status.Should().Be(PerformanceStatus.Late);
    }

    [Fact]
    public void Current_step_is_the_furthest_one_still_running()
    {
        var dates = new Dictionary<MilestoneType, DateOnly?>
        {
            [MilestoneType.Loading] = new DateOnly(2026, 3, 1),
            [MilestoneType.DepartureCustomsClearance] = new DateOnly(2026, 3, 2),
            [MilestoneType.Departure] = new DateOnly(2026, 3, 3)
        };

        var result = _evaluator.Evaluate(dates, _ => 5, Today, KpiStepCatalog.ShipmentSteps);

        result.CurrentStep!.Step.Should().Be(KpiStep.DepartureToArrival);
    }

    [Fact]
    public void Fully_delivered_shipment_inside_every_target_is_on_time_overall()
    {
        var dates = new Dictionary<MilestoneType, DateOnly?>
        {
            [MilestoneType.Loading] = new DateOnly(2026, 3, 1),
            [MilestoneType.DepartureCustomsClearance] = new DateOnly(2026, 3, 2),
            [MilestoneType.Departure] = new DateOnly(2026, 3, 3),
            [MilestoneType.ArrivalToTargetCountry] = new DateOnly(2026, 3, 5),
            [MilestoneType.CustomsStart] = new DateOnly(2026, 3, 6),
            [MilestoneType.CustomsEnd] = new DateOnly(2026, 3, 8),
            [MilestoneType.CrossdockArrival] = new DateOnly(2026, 3, 9)
        };

        var result = _evaluator.Evaluate(dates, step =>
            step == KpiStep.TotalLoadingToCrossdockArrival ? 15 : 3, Today, KpiStepCatalog.ShipmentSteps);

        result.Overall.Should().Be(PerformanceStatus.OnTime);
        result.BreachedSteps.Should().BeEmpty();
        result.CurrentStep.Should().BeNull();
    }
}
