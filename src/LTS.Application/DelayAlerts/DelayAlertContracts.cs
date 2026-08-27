namespace LTS.Application.DelayAlerts;

/// <summary>Which of the two delay alert mails a config/report belongs to.</summary>
public enum DelayAlertMailKind
{
    /// <summary>Shipments not yet arrived at Crossdock, scored on the 5 shipment-only KPI legs.</summary>
    Shipment,

    /// <summary>Transfers not yet arrived at their store, scored on {Xdock, Local Transportation}.</summary>
    Transfer
}

/// <summary>One country's settings for one delay alert mail, as shown/edited in the admin page.</summary>
public sealed record DelayAlertConfigRow
{
    public bool IsEnabled { get; init; }
    public string? Recipients { get; init; }
    public required TimeOnly SendTime { get; init; }
    public string? Subject { get; init; }
    public string? Body { get; init; }
    public DateOnly? LastSentDate { get; init; }
}

/// <summary>Values submitted when an administrator saves a delay alert config.</summary>
public sealed record DelayAlertConfigInput
{
    public bool IsEnabled { get; init; }
    public string? Recipients { get; init; }
    public required TimeOnly SendTime { get; init; }
    public string? Subject { get; init; }
    public string? Body { get; init; }
}

/// <summary>
/// Administration of the two delay alert mails' per-country settings. countryId is the app-wide
/// offset id (the same one CountryPageBase.CountryId exposes) - the implementation converts to
/// LTS_Integration's own raw id internally, the same convention IIntegrationKpiAdminService follows.
/// </summary>
public interface IDelayAlertAdminService
{
    Task<DelayAlertConfigRow> GetConfigAsync(
        int countryId, DelayAlertMailKind mailKind, CancellationToken cancellationToken = default);

    Task SaveAsync(
        int countryId, DelayAlertMailKind mailKind, DelayAlertConfigInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds and sends the report immediately, using the config's saved recipients/subject/body -
    /// ahead of its schedule, and without touching LastSentDate, so it never consumes that day's
    /// scheduled slot. Throws if nothing is configured/enabled or there are no recipients.
    /// </summary>
    Task SendNowAsync(
        int countryId, DelayAlertMailKind mailKind, CancellationToken cancellationToken = default);
}

/// <summary>One row of the Shipment delay alert Excel report - a shipment not yet at Crossdock Arrival, or within 7 days of it, currently Late/Overdue on its 5 shipment-only KPI legs.</summary>
public sealed record ShipmentDelayAlertRow(
    string InvoiceNo,
    string ReferenceNo,
    string? ExportType,
    string? LoadingPoint,
    string? ArrivalCustoms,
    string? TransportType,
    string? LogisticsCompany,
    string? BrokerCompany,
    string CurrentStatus,
    string DelayPhase,
    int DelayedDays,
    DateOnly DelayStartDate,
    DateOnly? DelayEndDate);

/// <summary>One row of the Transfer delay alert Excel report - a transfer not yet Store Arrived, or within 7 days of it, currently Late/Overdue on {Xdock, Local Transportation}.</summary>
public sealed record TransferDelayAlertRow(
    string InvoiceNo,
    string ReferenceNo,
    string TransferNo,
    string? ReceivingStore,
    string? ExportType,
    string? LoadingPoint,
    string? ArrivalCustoms,
    string? TransportType,
    string? LogisticsCompany,
    string? BrokerCompany,
    string CurrentStatus,
    string DelayPhase,
    int DelayedDays,
    DateOnly DelayStartDate,
    DateOnly? DelayEndDate);
