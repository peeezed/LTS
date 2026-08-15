using LTS.Domain.Enums;

namespace LTS.Domain.Kpi;

/// <summary>The measured intervals of the lifecycle, in order.</summary>
public static class KpiStepCatalog
{
    private static readonly KpiStepDefinition[] Definitions =
    [
        new(KpiStep.LoadingToExportClearance, MilestoneType.Loading, MilestoneType.DepartureCustomsClearance,
            MilestoneScope.Shipment, "Loading → Export Clearance"),

        new(KpiStep.ExportClearanceToDeparture, MilestoneType.DepartureCustomsClearance, MilestoneType.Departure,
            MilestoneScope.Shipment, "Export Clearance → Departure"),

        new(KpiStep.DepartureToArrival, MilestoneType.Departure, MilestoneType.ArrivalToTargetCountry,
            MilestoneScope.Shipment, "Departure → Arrival To Target Country"),

        new(KpiStep.ArrivalToCustomsStart, MilestoneType.ArrivalToTargetCountry, MilestoneType.CustomsStart,
            MilestoneScope.Shipment, "Arrival → Customs Start"),

        new(KpiStep.CustomsStartToCustomsEnd, MilestoneType.CustomsStart, MilestoneType.CustomsEnd,
            MilestoneScope.Shipment, "Customs Start → Customs End"),

        new(KpiStep.CustomsEndToCrossdockArrival, MilestoneType.CustomsEnd, MilestoneType.CrossdockArrival,
            MilestoneScope.Shipment, "Customs End → Crossdock Arrival"),

        // From here the shipment has been split, so the remaining legs are scored per transfer.
        new(KpiStep.CrossdockArrivalToCrossdockDeparture, MilestoneType.CrossdockArrival, MilestoneType.CrossdockDeparture,
            MilestoneScope.Transfer, "Crossdock Arrival → Crossdock Departure"),

        new(KpiStep.CrossdockDepartureToStoreArrival, MilestoneType.CrossdockDeparture, MilestoneType.StoreArrival,
            MilestoneScope.Transfer, "Crossdock Departure → Store Arrival"),

        new(KpiStep.StoreArrivalToPreAcceptance, MilestoneType.StoreArrival, MilestoneType.StorePreAcceptance,
            MilestoneScope.Transfer, "Store Arrival → Store Pre Acceptance"),

        new(KpiStep.PreAcceptanceToAcceptance, MilestoneType.StorePreAcceptance, MilestoneType.StoreAcceptance,
            MilestoneScope.Transfer, "Store Pre Acceptance → Store Acceptance"),

        new(KpiStep.TotalLoadingToCrossdockArrival, MilestoneType.Loading, MilestoneType.CrossdockArrival,
            MilestoneScope.Shipment, "Total: Loading → Crossdock Arrival", IsTotal: true),

        new(KpiStep.TotalLoadingToStoreAcceptance, MilestoneType.Loading, MilestoneType.StoreAcceptance,
            MilestoneScope.Transfer, "Total: Loading → Store Acceptance", IsTotal: true)
    ];

    private static readonly Dictionary<KpiStep, KpiStepDefinition> ByStep =
        Definitions.ToDictionary(d => d.Step);

    public static IReadOnlyList<KpiStepDefinition> All { get; } = Definitions;

    /// <summary>Steps scored on the shipment (everything up to crossdock arrival).</summary>
    public static IReadOnlyList<KpiStepDefinition> ShipmentSteps { get; } =
        [.. Definitions.Where(d => d.Scope == MilestoneScope.Shipment)];

    /// <summary>Steps scored on each transfer (the store leg, plus the end-to-end total).</summary>
    public static IReadOnlyList<KpiStepDefinition> TransferSteps { get; } =
        [.. Definitions.Where(d => d.Scope == MilestoneScope.Transfer)];

    public static KpiStepDefinition Get(KpiStep step) =>
        ByStep.TryGetValue(step, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(step), step, "Unknown KPI step.");

    public static string DisplayName(KpiStep step) => Get(step).DisplayName;

    public static IReadOnlyList<KpiStepDefinition> ForScope(MilestoneScope scope) =>
        scope == MilestoneScope.Shipment ? ShipmentSteps : TransferSteps;
}
