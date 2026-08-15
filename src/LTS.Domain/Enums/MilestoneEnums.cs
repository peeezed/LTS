namespace LTS.Domain.Enums;

/// <summary>
/// The standard, integration-independent set of tracked dates. Every country's
/// integration is normalised onto these values via <c>StatusMapping</c>, so screens,
/// KPIs and reports look identical regardless of the source system.
/// Values are spaced by 10 so a milestone can be inserted later without renumbering.
/// </summary>
public enum MilestoneType
{
    // --- Shipment scope: loading through crossdock arrival ---
    Loading = 10,
    DepartureCustomsClearance = 20,
    Departure = 30,
    ArrivalToTargetCountry = 40,
    CustomsStart = 50,
    CustomsEnd = 60,
    CrossdockArrival = 70,

    // --- Transfer scope: crossdock departure through store acceptance ---
    CrossdockDeparture = 80,
    PlannedStoreArrival = 85,
    StoreArrival = 90,
    StorePreAcceptance = 100,
    StoreAcceptance = 110
}

/// <summary>Which entity a milestone is recorded against.</summary>
public enum MilestoneScope
{
    Shipment = 1,
    Transfer = 2
}

/// <summary>
/// Who is responsible for supplying a milestone date. Drives field-level visibility on
/// the Shipment Details page and validation of Excel uploads.
/// </summary>
public enum MilestoneOwner
{
    /// <summary>Entered by the logistics company that moves the shipment.</summary>
    LogisticsCompany = 1,

    /// <summary>Entered by the customs broker.</summary>
    Broker = 2,

    /// <summary>Received from the warehouse integration, or entered manually by the logistics department.</summary>
    Warehouse = 3,

    /// <summary>Received from the in-house company service only — never entered by hand.</summary>
    InHouseService = 4
}

/// <summary>Provenance of a milestone value, recorded on every write in <c>MilestoneAudit</c>.</summary>
public enum MilestoneSource
{
    Manual = 1,
    ExcelUpload = 2,
    Integration = 3,
    InHouseService = 4,
    Seed = 5
}
