using LTS.Application.Integration;
using LTS.Domain.Entities;
using LTS.Domain.Enums;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Integration;

public sealed class IntegrationAdminService(
    LtsDbContext db,
    IIntegrationAdapterRegistry adapters,
    IntegrationRunner runner) : IIntegrationAdminService
{
    public IReadOnlyList<string> AvailableAdapterKeys => adapters.RegisteredKeys;

    public async Task<IReadOnlyList<IntegrationSourceRow>> GetSourcesAsync(
        int countryId, CancellationToken cancellationToken = default)
    {
        var rows = await db.IntegrationSources
            .AsNoTracking()
            .Where(s => s.CountryId == countryId)
            .Select(s => new
            {
                Source = s,
                MappingCount = db.StatusMappings.Count(m => m.IntegrationSourceId == s.Id)
            })
            .OrderBy(x => x.Source.Name)
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(x => new IntegrationSourceRow
            {
                Id = x.Source.Id,
                Name = x.Source.Name,
                Kind = x.Source.Kind,
                AdapterKey = x.Source.AdapterKey,
                BaseUrl = x.Source.BaseUrl,
                PollIntervalMinutes = x.Source.PollIntervalMinutes,
                IsActive = x.Source.IsActive,
                ManualOverrideWins = x.Source.ManualOverrideWins,
                LastRunAt = x.Source.LastRunAt,
                LastSuccessAt = x.Source.LastSuccessAt,
                MappingCount = x.MappingCount,
                // A source pointing at an adapter that no longer exists would fail silently at
                // poll time, so it is flagged where it can be seen and fixed.
                AdapterMissing = adapters.Find(x.Source.AdapterKey) is null
            })
        ];
    }

    public async Task<int> SaveSourceAsync(IntegrationSourceInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var source = input.Id is { } id
            ? await db.IntegrationSources.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new InvalidOperationException($"Integration source {id} does not exist.")
            : new IntegrationSource
            {
                CountryId = input.CountryId,
                Kind = input.Kind,
                Name = input.Name,
                AdapterKey = input.AdapterKey
            };

        if (input.Id is null)
        {
            db.IntegrationSources.Add(source);
        }

        source.CountryId = input.CountryId;
        source.Name = input.Name;
        source.Kind = input.Kind;
        source.AdapterKey = input.AdapterKey;
        source.BaseUrl = input.BaseUrl;
        source.SecretName = input.SecretName;
        source.SettingsJson = input.SettingsJson;
        source.PollIntervalMinutes = Math.Max(1, input.PollIntervalMinutes);
        source.IsActive = input.IsActive;
        source.ManualOverrideWins = input.ManualOverrideWins;

        await db.SaveChangesAsync(cancellationToken);

        return source.Id;
    }

    public async Task<IReadOnlyList<StatusMappingRow>> GetMappingsAsync(
        int integrationSourceId, CancellationToken cancellationToken = default) =>
        await db.StatusMappings
            .AsNoTracking()
            .Where(m => m.IntegrationSourceId == integrationSourceId)
            .OrderBy(m => m.RawCode)
            .Select(m => new StatusMappingRow
            {
                Id = m.Id,
                IntegrationSourceId = m.IntegrationSourceId,
                RawCode = m.RawCode,
                RawDescription = m.RawDescription,
                MilestoneType = m.MilestoneType,
                IsIgnored = m.IsIgnored,
                IsActive = m.IsActive
            })
            .ToListAsync(cancellationToken);

    public async Task<int> SaveMappingAsync(StatusMappingInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var rawCode = input.RawCode.Trim();
        if (rawCode.Length == 0)
        {
            throw new ArgumentException("A raw status code is required.", nameof(input));
        }

        var mapping = input.Id is { } id
            ? await db.StatusMappings.FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
                ?? throw new InvalidOperationException($"Status mapping {id} does not exist.")
            : new StatusMapping { IntegrationSourceId = input.IntegrationSourceId, RawCode = rawCode };

        if (input.Id is null)
        {
            var duplicate = await db.StatusMappings.AnyAsync(
                m => m.IntegrationSourceId == input.IntegrationSourceId && m.RawCode == rawCode,
                cancellationToken);

            if (duplicate)
            {
                throw new InvalidOperationException($"'{rawCode}' is already mapped for this source.");
            }

            db.StatusMappings.Add(mapping);
        }

        mapping.RawCode = rawCode;
        mapping.RawDescription = input.RawDescription;
        mapping.MilestoneType = input.IsIgnored ? null : input.MilestoneType;
        mapping.IsIgnored = input.IsIgnored;
        mapping.IsActive = input.IsActive;

        await db.SaveChangesAsync(cancellationToken);

        return mapping.Id;
    }

    public async Task DeleteMappingAsync(int id, CancellationToken cancellationToken = default)
    {
        var mapping = await db.StatusMappings.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (mapping is null)
        {
            return;
        }

        db.StatusMappings.Remove(mapping);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IntegrationRunRow>> GetRunsAsync(
        int countryId, int take = 50, CancellationToken cancellationToken = default) =>
        await db.IntegrationRuns
            .AsNoTracking()
            .Where(r => r.IntegrationSource!.CountryId == countryId)
            .OrderByDescending(r => r.StartedAt)
            .Take(take)
            .Select(r => new IntegrationRunRow
            {
                Id = r.Id,
                SourceName = r.IntegrationSource!.Name,
                Status = r.Status,
                StartedAt = r.StartedAt,
                FinishedAt = r.FinishedAt,
                MessagesReceived = r.MessagesReceived,
                MessagesProcessed = r.MessagesProcessed,
                MessagesFailed = r.MessagesFailed,
                ShipmentsCreated = r.ShipmentsCreated,
                ShipmentsUpdated = r.ShipmentsUpdated,
                TransfersCreated = r.TransfersCreated,
                MilestonesApplied = r.MilestonesApplied,
                UnmappedCodeCount = r.UnmappedCodeCount,
                ErrorMessage = r.ErrorMessage
            })
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<UnmappedCode>> GetUnmappedCodesAsync(
        int countryId, CancellationToken cancellationToken = default)
    {
        var rows = await db.IntegrationMessages
            .AsNoTracking()
            .Where(m => m.Status == IntegrationMessageStatus.Skipped
                        && m.RawStatusCode != null
                        && m.IntegrationRun!.IntegrationSource!.CountryId == countryId)
            .GroupBy(m => new
            {
                m.IntegrationRun!.IntegrationSourceId,
                SourceName = m.IntegrationRun.IntegrationSource!.Name,
                m.RawStatusCode
            })
            .Select(g => new
            {
                g.Key.IntegrationSourceId,
                g.Key.SourceName,
                RawCode = g.Key.RawStatusCode!,
                Occurrences = g.Count()
            })
            .ToListAsync(cancellationToken);

        // Codes mapped since those messages were recorded are no longer a problem, so they are
        // filtered out rather than nagging forever.
        var mapped = await db.StatusMappings
            .AsNoTracking()
            .Select(m => new { m.IntegrationSourceId, m.RawCode })
            .ToListAsync(cancellationToken);

        var mappedKeys = mapped
            .Select(m => (m.IntegrationSourceId, m.RawCode.ToUpperInvariant()))
            .ToHashSet();

        return
        [
            .. rows
                .Where(r => !mappedKeys.Contains((r.IntegrationSourceId, r.RawCode.ToUpperInvariant())))
                .OrderByDescending(r => r.Occurrences)
                .Select(r => new UnmappedCode(r.IntegrationSourceId, r.SourceName, r.RawCode, r.Occurrences))
        ];
    }

    public async Task<IntegrationRunRow> RunNowAsync(
        int integrationSourceId, CancellationToken cancellationToken = default)
    {
        var run = await runner.RunAsync(integrationSourceId, cancellationToken);

        var name = await db.IntegrationSources
            .AsNoTracking()
            .Where(s => s.Id == integrationSourceId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        return new IntegrationRunRow
        {
            Id = run.Id,
            SourceName = name,
            Status = run.Status,
            StartedAt = run.StartedAt,
            FinishedAt = run.FinishedAt,
            MessagesReceived = run.MessagesReceived,
            MessagesProcessed = run.MessagesProcessed,
            MessagesFailed = run.MessagesFailed,
            ShipmentsCreated = run.ShipmentsCreated,
            ShipmentsUpdated = run.ShipmentsUpdated,
            TransfersCreated = run.TransfersCreated,
            MilestonesApplied = run.MilestonesApplied,
            UnmappedCodeCount = run.UnmappedCodeCount,
            ErrorMessage = run.ErrorMessage
        };
    }
}
