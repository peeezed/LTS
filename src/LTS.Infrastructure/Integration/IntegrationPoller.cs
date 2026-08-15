using LTS.Application.Abstractions;
using LTS.Infrastructure.Configuration;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LTS.Infrastructure.Integration;

/// <summary>
/// Wakes up on a fixed tick and runs any integration source whose own interval has elapsed.
/// LTS pulls rather than being pushed to, so no country has to open a route into this network
/// or change its systems to send anything.
/// </summary>
public sealed class IntegrationPoller(
    IServiceScopeFactory scopeFactory,
    IOptions<LtsOptions> options,
    IClock clock,
    ILogger<IntegrationPoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.Integration;

        if (!settings.Enabled)
        {
            logger.LogInformation("Integration polling is disabled by configuration.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(10, settings.PollSeconds));
        logger.LogInformation("Integration poller started; checking for due sources every {Interval}.", interval);

        using var timer = new PeriodicTimer(interval);

        // The first tick is skipped deliberately: startup already has migrations and seeding to
        // get through, and nothing is due in the first few seconds anyway.
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PollDueSourcesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // One bad source must not take the poller down for every country.
                logger.LogError(exception, "Integration poll cycle failed.");
            }
        }
    }

    private async Task PollDueSourcesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LtsDbContext>();

        var now = clock.UtcNow;

        var dueIds = await db.IntegrationSources
            .AsNoTracking()
            .Where(s => s.IsActive)
            .Select(s => new { s.Id, s.LastRunAt, s.PollIntervalMinutes })
            .ToListAsync(cancellationToken);

        foreach (var source in dueIds)
        {
            if (source.LastRunAt is { } lastRun && lastRun.AddMinutes(source.PollIntervalMinutes) > now)
            {
                continue;
            }

            // A fresh scope per source keeps one source's DbContext state out of the next one's.
            using var runScope = scopeFactory.CreateScope();
            var runner = runScope.ServiceProvider.GetRequiredService<IntegrationRunner>();

            var run = await runner.RunAsync(source.Id, cancellationToken);

            logger.LogInformation(
                "Integration source {SourceId} finished {Status}: {Processed} processed, {Failed} failed, " +
                "{Milestones} milestones applied, {Unmapped} unmapped code(s).",
                source.Id, run.Status, run.MessagesProcessed, run.MessagesFailed,
                run.MilestonesApplied, run.UnmappedCodeCount);
        }
    }
}
