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
