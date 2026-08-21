using LTS.Domain.Enums;

namespace LTS.Domain.Kpi;

/// <summary>One of the six LTS_Integration KPI legs: which two milestones bound it.</summary>
public sealed record IntegrationKpiStepDefinition(
    IntegrationKpiStep Step, string DisplayName, MilestoneType From, MilestoneType To);

/// <summary>
/// The six-leg KPI model LTS_Integration scores against, matching the KPI*Date deadline columns
/// already present on LTS_ShipmentDates/LTS_ShipmentTransferDates. Mirrors MilestoneCatalog's shape.
/// </summary>
public static class IntegrationKpiCatalog
{
    private static readonly IntegrationKpiStepDefinition[] Definitions =
    [
        new(IntegrationKpiStep.LoadingToCustomsClearance, "Loading to Customs Clearance",
            MilestoneType.Loading, MilestoneType.DepartureCustomsClearance),

        new(IntegrationKpiStep.CustomsToDeparture, "Customs to Customs Departure",
            MilestoneType.DepartureCustomsClearance, MilestoneType.Departure),

        new(IntegrationKpiStep.InternationalTransportation, "International Transportation",
            MilestoneType.Departure, MilestoneType.ArrivalToTargetCountry),

        new(IntegrationKpiStep.CountryCustomsClearance, "Country Customs Clearance",
            MilestoneType.CustomsStart, MilestoneType.CustomsEnd),

        new(IntegrationKpiStep.LeadTimeToXdock, "Lead Time to XDock",
            MilestoneType.CustomsEnd, MilestoneType.CrossdockArrival),

        // The one exception: From (CrossdockArrival) is shipment-scope but To (CrossdockDeparture)
        // is transfer-scope - IntegrationKpiCalculator checks MilestoneCatalog.Get(To).Scope to
        // know to read the transfer, not the shipment, for the actual end date and its deadline.
        new(IntegrationKpiStep.Xdock, "XDock",
            MilestoneType.CrossdockArrival, MilestoneType.CrossdockDeparture)
    ];

    private static readonly Dictionary<IntegrationKpiStep, IntegrationKpiStepDefinition> ByStep =
        Definitions.ToDictionary(d => d.Step);

    public static IReadOnlyList<IntegrationKpiStepDefinition> All { get; } = Definitions;

    public static IntegrationKpiStepDefinition Get(IntegrationKpiStep step) =>
        ByStep.TryGetValue(step, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(step), step, "Unknown KPI step.");
}
