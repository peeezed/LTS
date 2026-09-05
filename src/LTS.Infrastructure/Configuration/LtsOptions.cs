namespace LTS.Infrastructure.Configuration;

/// <summary>Application settings, bound from the "Lts" configuration section.</summary>
public sealed class LtsOptions
{
    public const string SectionName = "Lts";

    public AdminSeedOptions Admin { get; set; } = new();

    public ShipmentFeedOptions ShipmentFeed { get; set; } = new();

    public ExportAttributeFeedOptions ExportAttributeFeed { get; set; } = new();

    public ShipmentStatusReconciliationOptions ShipmentStatusReconciliation { get; set; } = new();

    public MailOptions Mail { get; set; } = new();

    public DelayAlertOptions DelayAlerts { get; set; } = new();

    public RomaniaOneClickOptions RomaniaOneClick { get; set; } = new();
}

/// <summary>The first administrator, created on an empty database so someone can log in.</summary>
public sealed class AdminSeedOptions
{
    public string Email { get; set; } = "admin@lts.local";

    public string FullName { get; set; } = "LTS Administrator";

    /// <summary>
    /// Issued with "must change password" set, so the seeded value cannot survive first login.
    /// </summary>
    public string InitialPassword { get; set; } = "ChangeMe!2026";
}

/// <summary>
/// How the shipments feed poller behaves - the company's own internal shipment header source,
/// pulled into LTS_Shipments via LTS_ShipmentFeedStaging.
/// </summary>
public sealed class ShipmentFeedOptions
{
    public bool Enabled { get; set; }

    /// <summary>Base URL of the shared internal shipments endpoint.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Name of the entry under Integration:Secrets carrying the bearer token.</summary>
    public string? SecretName { get; set; }

    public int PollSeconds { get; set; } = 300;
}

/// <summary>
/// How the export attribute feed poller behaves - finds shipments missing a required KPI-scoping
/// attribute and backfills them from GetLTSExportFileDetail, one shipment at a time by reference
/// number. Unrelated to <see cref="ShipmentFeedOptions"/> even though it may share the same host:
/// different endpoint, different trigger condition, its own poll cycle.
/// </summary>
public sealed class ExportAttributeFeedOptions
{
    public bool Enabled { get; set; }

    /// <summary>Base URL of the shared internal shipments endpoint.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Name of the entry under Integration:Secrets carrying the bearer token.</summary>
    public string? SecretName { get; set; }

    /// <summary>
    /// Longer than ShipmentFeedOptions.PollSeconds by default - backfilling a missing attribute is
    /// not as time-sensitive as picking up a brand-new shipment.
    /// </summary>
    public int PollSeconds { get; set; } = 600;
}

/// <summary>
/// How the shipment status reconciliation poller behaves - catches up LTS_Shipments.CurrentStatus
/// for shipments whose transfer dates changed outside the app (see ShipmentStatusReconciler).
/// </summary>
public sealed class ShipmentStatusReconciliationOptions
{
    /// <summary>
    /// On by default: unlike the other pollers, this one has no external dependency (no endpoint,
    /// no secret) to misconfigure - it only reads/writes LTS_Integration, which is already
    /// required for the app to run at all.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public int PollSeconds { get; set; } = 60;
}

/// <summary>
/// SMTP settings used to send every outgoing email (currently just the delay alert mails). The
/// password is never stored here - it's read via Integration:Secrets:{SecretName}, the same
/// convention ShipmentFeedClient/ExportAttributeFeedClient already use for their bearer tokens.
/// </summary>
public sealed class MailOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public bool UseSsl { get; set; } = true;

    public string? Username { get; set; }

    /// <summary>Name of the entry under Integration:Secrets carrying the SMTP password.</summary>
    public string? SecretName { get; set; }

    public string? FromAddress { get; set; }

    public string? FromName { get; set; }
}

/// <summary>
/// How the delay alert poller behaves - CheckIntervalSeconds is just how often it wakes up to see
/// whether any per-country LTS_DelayAlertConfigs row is due, not the mail's own send time (that's
/// each config's own SendTime, configured per country in the admin page).
/// </summary>
public sealed class DelayAlertOptions
{
    public bool Enabled { get; set; } = true;

    public int CheckIntervalSeconds { get; set; } = 60;
}

/// <summary>
/// How the Romania KLG OneClick poller behaves - a genuinely third-party, OAuth-style API, unlike
/// every other integration here. One KLG "domestic shipment" corresponds to one LTS transfer (a
/// crossdock-to-store leg), matched by <c>RomaniaPermShipmentId</c> typed onto the transfer by
/// hand (see Transfers.razor) - so this poller looks shipments up one at a time by that id, it
/// never lists.
/// </summary>
public sealed class RomaniaOneClickOptions
{
    public bool Enabled { get; set; }

    /// <summary>KLG OneClick's base API URL, e.g. https://api.oneclick.ro/v1.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Name of the entry under Integration:Secrets carrying the initial, manually-generated access
    /// token. Kept only for reference - RomaniaTokenStore never reads it at runtime, since it
    /// bootstraps straight from the refresh key below (see RomaniaTokenStore.GetValidAccessTokenAsync)
    /// and never trusts an access token whose actual remaining lifetime it cannot verify.
    /// </summary>
    public string? ApiKeySecretName { get; set; }

    /// <summary>Name of the entry under Integration:Secrets carrying the initial refresh token.</summary>
    public string? RefreshKeySecretName { get; set; }

    public int PollSeconds { get; set; } = 3600;
}
