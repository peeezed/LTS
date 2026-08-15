using LTS.Domain.Enums;

namespace LTS.Domain.Milestones;

/// <summary>
/// The standard LTS lifecycle, in order. Countries differ in how dates arrive, never in what
/// the dates mean, so this catalog is fixed in code and shared by every country.
/// </summary>
public static class MilestoneCatalog
{
    private static readonly MilestoneDefinition[] Definitions =
    [
        // Shipment half of the lifecycle: loading through crossdock arrival.
        new(MilestoneType.Loading, MilestoneScope.Shipment, MilestoneOwner.LogisticsCompany,
            10, "Loading", TrackingStatus.Loaded, AllowsManualEntry: true),

        new(MilestoneType.DepartureCustomsClearance, MilestoneScope.Shipment, MilestoneOwner.LogisticsCompany,
            20, "Departure Country Customs Clearance", TrackingStatus.ExportCustomsCleared, AllowsManualEntry: true),

        new(MilestoneType.Departure, MilestoneScope.Shipment, MilestoneOwner.LogisticsCompany,
            30, "Departure", TrackingStatus.Departed, AllowsManualEntry: true),

        new(MilestoneType.ArrivalToTargetCountry, MilestoneScope.Shipment, MilestoneOwner.LogisticsCompany,
            40, "Arrival To Target Country", TrackingStatus.ArrivedInTargetCountry, AllowsManualEntry: true),

        new(MilestoneType.CustomsStart, MilestoneScope.Shipment, MilestoneOwner.Broker,
            50, "Customs Start", TrackingStatus.CustomsInProgress, AllowsManualEntry: true),

        new(MilestoneType.CustomsEnd, MilestoneScope.Shipment, MilestoneOwner.Broker,
            60, "Customs End", TrackingStatus.CustomsCleared, AllowsManualEntry: true),

        new(MilestoneType.CrossdockArrival, MilestoneScope.Shipment, MilestoneOwner.Warehouse,
            70, "Crossdock Arrival", TrackingStatus.AtCrossdock, AllowsManualEntry: true),

        // Transfer half: the shipment is split at the crossdock and each store leg is tracked separately.
        new(MilestoneType.CrossdockDeparture, MilestoneScope.Transfer, MilestoneOwner.Warehouse,
            80, "Crossdock Departure", TrackingStatus.InTransitToStore, AllowsManualEntry: true),

        new(MilestoneType.PlannedStoreArrival, MilestoneScope.Transfer, MilestoneOwner.Warehouse,
            85, "Planned Store Arrival", ReachedStatus: null, AllowsManualEntry: true),

        new(MilestoneType.StoreArrival, MilestoneScope.Transfer, MilestoneOwner.Warehouse,
            90, "Store Arrival", TrackingStatus.ArrivedAtStore, AllowsManualEntry: true),

        new(MilestoneType.StorePreAcceptance, MilestoneScope.Transfer, MilestoneOwner.InHouseService,
            100, "Store Pre Acceptance", TrackingStatus.PreAccepted, AllowsManualEntry: false),

        new(MilestoneType.StoreAcceptance, MilestoneScope.Transfer, MilestoneOwner.InHouseService,
            110, "Store Acceptance", TrackingStatus.Accepted, AllowsManualEntry: false)
    ];

    private static readonly Dictionary<MilestoneType, MilestoneDefinition> ByType =
        Definitions.ToDictionary(d => d.Type);

    /// <summary>Every milestone, in lifecycle order.</summary>
    public static IReadOnlyList<MilestoneDefinition> All { get; } = [.. Definitions.OrderBy(d => d.Sequence)];

    /// <summary>Milestones recorded on the shipment (loading through crossdock arrival).</summary>
    public static IReadOnlyList<MilestoneDefinition> ShipmentMilestones { get; } =
        [.. All.Where(d => d.Scope == MilestoneScope.Shipment)];

    /// <summary>Milestones recorded on each transfer (crossdock departure through store acceptance).</summary>
    public static IReadOnlyList<MilestoneDefinition> TransferMilestones { get; } =
        [.. All.Where(d => d.Scope == MilestoneScope.Transfer)];

    public static MilestoneDefinition Get(MilestoneType type) =>
        ByType.TryGetValue(type, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown milestone type.");

    public static string DisplayName(MilestoneType type) => Get(type).DisplayName;

    public static IReadOnlyList<MilestoneDefinition> ForScope(MilestoneScope scope) =>
        scope == MilestoneScope.Shipment ? ShipmentMilestones : TransferMilestones;

    /// <summary>Milestones a given owner is responsible for, in lifecycle order.</summary>
    public static IReadOnlyList<MilestoneDefinition> ForOwner(MilestoneOwner owner) =>
        [.. All.Where(d => d.Owner == owner)];

    /// <summary>
    /// The milestone owner a user type supplies dates for, or <c>null</c> if the user type is not
    /// tied to one specific owner (admins and the logistics department cover the manual-entry fields).
    /// </summary>
    public static MilestoneOwner? OwnerForUserType(UserType userType) => userType switch
    {
        UserType.Broker => MilestoneOwner.Broker,
        UserType.LogisticsCompany => MilestoneOwner.LogisticsCompany,
        _ => null
    };
}
