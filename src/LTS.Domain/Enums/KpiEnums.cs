namespace LTS.Domain.Enums;

/// <summary>
/// A measured interval between two milestones. KPI targets are given per step in days by
/// the logistics department, keyed by export type + loading country + arrival country.
/// </summary>
public enum KpiStep
{
    LoadingToExportClearance = 10,
    ExportClearanceToDeparture = 20,
    DepartureToArrival = 30,
    ArrivalToCustomsStart = 40,
    CustomsStartToCustomsEnd = 50,
    CustomsEndToCrossdockArrival = 60,
    CrossdockArrivalToCrossdockDeparture = 70,
    CrossdockDepartureToStoreArrival = 80,
    StoreArrivalToPreAcceptance = 90,
    PreAcceptanceToAcceptance = 100,

    /// <summary>End-to-end target for the shipment half of the lifecycle.</summary>
    TotalLoadingToCrossdockArrival = 900,

    /// <summary>End-to-end target from loading all the way to store acceptance.</summary>
    TotalLoadingToStoreAcceptance = 910
}

/// <summary>
/// Outcome of comparing an actual duration against its KPI target.
/// Completed steps resolve to <see cref="OnTime"/> or <see cref="Late"/>; steps still running
/// resolve to <see cref="OnTrack"/>, <see cref="AtRisk"/> or <see cref="Overdue"/>.
/// </summary>
public enum PerformanceStatus
{
    /// <summary>The step has not begun — its start milestone has no date yet.</summary>
    NotStarted = 0,

    /// <summary>No KPI target matches this shipment for this step, so it cannot be scored.</summary>
    NoTarget = 1,

    /// <summary>In progress and comfortably inside the target.</summary>
    OnTrack = 2,

    /// <summary>In progress and at or past <see cref="Kpi.KpiEvaluator.AtRiskThreshold"/> of the target.</summary>
    AtRisk = 3,

    /// <summary>In progress and already past the target.</summary>
    Overdue = 4,

    /// <summary>Completed within the target.</summary>
    OnTime = 5,

    /// <summary>Completed past the target.</summary>
    Late = 6,

    /// <summary>
    /// One of the shipment's required KPI-scoping attributes (Export Type, Loading Point, Arrival
    /// Customs, Transport Type) is missing, so no leg could be resolved against a target at all -
    /// distinct from <see cref="NoTarget"/>, which means resolution ran but nothing matched.
    /// </summary>
    MissingAttributes = 7
}
