using LTS.Domain.Enums;

namespace LTS.Application.Integration;

/// <summary>An integration source as shown on the admin page.</summary>
public sealed record IntegrationSourceRow
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required IntegrationSourceKind Kind { get; init; }
    public required string AdapterKey { get; init; }
    public string? BaseUrl { get; init; }
    public int PollIntervalMinutes { get; init; }
    public bool IsActive { get; init; }
    public bool ManualOverrideWins { get; init; }
    public DateTime? LastRunAt { get; init; }
    public DateTime? LastSuccessAt { get; init; }
    public int MappingCount { get; init; }

    /// <summary>True when the configured adapter key has no implementation registered.</summary>
    public bool AdapterMissing { get; init; }
}

/// <summary>Editable settings of an integration source.</summary>
public sealed record IntegrationSourceInput
{
    public int? Id { get; init; }
    public required int CountryId { get; init; }
    public required string Name { get; init; }
    public required IntegrationSourceKind Kind { get; init; }
    public required string AdapterKey { get; init; }
    public string? BaseUrl { get; init; }
    public string? SecretName { get; init; }
    public string? SettingsJson { get; init; }
    public int PollIntervalMinutes { get; init; } = 15;
    public bool IsActive { get; init; } = true;
    public bool ManualOverrideWins { get; init; }
}

/// <summary>One raw code and the LTS milestone an administrator decided it means.</summary>
public sealed record StatusMappingRow
{
    public required int Id { get; init; }
    public required int IntegrationSourceId { get; init; }
    public required string RawCode { get; init; }
    public string? RawDescription { get; init; }
    public MilestoneType? MilestoneType { get; init; }
    public bool IsIgnored { get; init; }
    public bool IsActive { get; init; }
}

public sealed record StatusMappingInput
{
    public int? Id { get; init; }
    public required int IntegrationSourceId { get; init; }
    public required string RawCode { get; init; }
    public string? RawDescription { get; init; }
    public MilestoneType? MilestoneType { get; init; }
    public bool IsIgnored { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>A poll, as shown on the integration monitor.</summary>
public sealed record IntegrationRunRow
{
    public required int Id { get; init; }
    public required string SourceName { get; init; }
    public required IntegrationRunStatus Status { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public int MessagesReceived { get; init; }
    public int MessagesProcessed { get; init; }
    public int MessagesFailed { get; init; }
    public int ShipmentsCreated { get; init; }
    public int ShipmentsUpdated { get; init; }
    public int TransfersCreated { get; init; }
    public int MilestonesApplied { get; init; }
    public int UnmappedCodeCount { get; init; }
    public string? ErrorMessage { get; init; }

    public TimeSpan? Duration => FinishedAt - StartedAt;
}

/// <summary>A code a source sent that nothing maps, ready to be turned into a mapping.</summary>
public sealed record UnmappedCode(int IntegrationSourceId, string SourceName, string RawCode, int Occurrences);

/// <summary>
/// Administration of the integration layer: which systems a country pulls from, what their
/// status codes mean in LTS, and what each poll did.
/// </summary>
public interface IIntegrationAdminService
{
    Task<IReadOnlyList<IntegrationSourceRow>> GetSourcesAsync(
        int countryId, CancellationToken cancellationToken = default);

    Task<int> SaveSourceAsync(IntegrationSourceInput input, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StatusMappingRow>> GetMappingsAsync(
        int integrationSourceId, CancellationToken cancellationToken = default);

    Task<int> SaveMappingAsync(StatusMappingInput input, CancellationToken cancellationToken = default);

    Task DeleteMappingAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IntegrationRunRow>> GetRunsAsync(
        int countryId, int take = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Codes seen in recent runs with no mapping. Surfacing these is what turns "the integration
    /// is dropping something" into a specific, fixable list.
    /// </summary>
    Task<IReadOnlyList<UnmappedCode>> GetUnmappedCodesAsync(
        int countryId, CancellationToken cancellationToken = default);

    /// <summary>Runs a source immediately instead of waiting for its next scheduled poll.</summary>
    Task<IntegrationRunRow> RunNowAsync(int integrationSourceId, CancellationToken cancellationToken = default);

    /// <summary>Adapter keys that have an implementation registered.</summary>
    IReadOnlyList<string> AvailableAdapterKeys { get; }
}
