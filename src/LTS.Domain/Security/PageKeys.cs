namespace LTS.Domain.Security;

/// <summary>Stable identifiers for every permission-controlled page.</summary>
public static class PageKeys
{
    public const string Shipments = "shipments";
    public const string Transfers = "transfers";
    public const string ShipmentsOnTheWay = "shipments-on-the-way";
    public const string ShipmentDetails = "shipment-details";
    public const string DateUpload = "date-upload";

    public const string AdminUsers = "admin.users";
    public const string AdminCountries = "admin.countries";
    public const string AdminMasterData = "admin.master-data";
    public const string AdminKpi = "admin.kpi";
    public const string AdminDelayAlerts = "admin.delay-alerts";
    public const string AdminAuditLog = "admin.audit-log";
}

/// <summary>
/// A page an admin can grant access to.
/// </summary>
/// <param name="Key">Stable key stored in permission rows.</param>
/// <param name="DisplayName">Label in the permission editor and navigation.</param>
/// <param name="Group">Groups the permission editor's rows.</param>
/// <param name="IsCountryScoped">
/// False for cross-country administration, where permissions are granted once rather than per country.
/// </param>
/// <param name="SupportsEdit">False for read-only pages, where the Edit checkbox is meaningless.</param>
public sealed record PageDefinition(
    string Key,
    string DisplayName,
    string Group,
    bool IsCountryScoped = true,
    bool SupportsEdit = true);

/// <summary>Every permission-controlled page, in navigation order.</summary>
public static class PageCatalog
{
    public const string TrackingGroup = "Tracking";
    public const string AdminGroup = "Administration";

    public static IReadOnlyList<PageDefinition> All { get; } =
    [
        new(PageKeys.Shipments, "Shipments", TrackingGroup, SupportsEdit: false),
        new(PageKeys.Transfers, "Transfers", TrackingGroup, SupportsEdit: false),
        new(PageKeys.ShipmentsOnTheWay, "Shipments On The Way", TrackingGroup, SupportsEdit: false),
        new(PageKeys.ShipmentDetails, "Shipment Details", TrackingGroup),
        new(PageKeys.DateUpload, "Date Upload", TrackingGroup),

        new(PageKeys.AdminUsers, "Users & Permissions", AdminGroup, IsCountryScoped: false),
        new(PageKeys.AdminCountries, "Countries", AdminGroup, IsCountryScoped: false),
        new(PageKeys.AdminMasterData, "Master Data", AdminGroup),
        new(PageKeys.AdminKpi, "KPI Targets", AdminGroup),
        new(PageKeys.AdminDelayAlerts, "Delay Alerts", AdminGroup),
        new(PageKeys.AdminAuditLog, "Audit Log", AdminGroup, SupportsEdit: false)
    ];

    public static PageDefinition Get(string key) =>
        All.FirstOrDefault(p => p.Key == key)
        ?? throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown page key.");

    public static bool IsCountryScoped(string key) => Get(key).IsCountryScoped;
}
