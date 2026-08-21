namespace LTS.Infrastructure.Configuration;

/// <summary>Application settings, bound from the "Lts" configuration section.</summary>
public sealed class LtsOptions
{
    public const string SectionName = "Lts";

    /// <summary>
    /// Applies pending migrations at startup. Convenient for a single-instance deployment;
    /// turn off where migrations are run as a separate release step.
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; set; } = true;

    /// <summary>
    /// Generates the demo country, reference data and shipments. Development only — it is what
    /// makes the grids, KPIs and dashboard testable before a real integration exists.
    /// </summary>
    public bool SeedDemoData { get; set; }

    public AdminSeedOptions Admin { get; set; } = new();

    public IntegrationOptions Integration { get; set; } = new();

    public ShipmentFeedOptions ShipmentFeed { get; set; } = new();

    public ShipmentStatusReconciliationOptions ShipmentStatusReconciliation { get; set; } = new();
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

/// <summary>How the integration poller behaves.</summary>
public sealed class IntegrationOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How often the poller wakes up to look for due sources. Each source then runs on its own
    /// <c>PollIntervalMinutes</c>, so this only bounds how promptly one becomes due.
    /// </summary>
    public int PollSeconds { get; set; } = 60;

    /// <summary>Folder the mock adapter reads sample payloads from, relative to the content root.</summary>
    public string MockDataPath { get; set; } = "SampleData";
}

/// <summary>
/// How the shipments feed poller behaves - the company's own internal shipment header source,
/// pulled into LTS_Shipments via LTS_ShipmentFeedStaging. Unrelated to (and simpler than)
/// <see cref="IntegrationOptions"/>, which belongs to the old, now-dead country-adapter pipeline.
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
