using LTS.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LTS.Infrastructure.ExportAttributeFeed;

/// <summary>
/// Wakes up on a fixed tick and runs one full ExportAttributeFeedRunner pass. Config-driven
/// (Lts:ExportAttributeFeed), structurally identical to ShipmentFeedPoller but on its own schedule.
/// </summary>
public sealed class ExportAttributeFeedPoller(
    IServiceScopeFactory scopeFactory,
    IOptions<LtsOptions> options,
    ILogger<ExportAttributeFeedPoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.ExportAttributeFeed;

        if (!settings.Enabled)
        {
            logger.LogInformation("Export attribute feed polling is disabled by configuration.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(10, settings.PollSeconds));
        logger.LogInformation("Export attribute feed poller started; polling every {Interval}.", interval);

        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<ExportAttributeFeedRunner>();
                await runner.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Backstop only - ExportAttributeFeedRunner already isolates per-shipment failures.
                logger.LogError(exception, "Export attribute feed poll cycle failed.");
            }
        }
    }
}
