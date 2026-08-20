using LTS.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LTS.Infrastructure.ShipmentFeed;

/// <summary>
/// Wakes up on a fixed tick and runs one full ShipmentFeedRunner pass. Config-driven (Lts:
/// ShipmentFeed) rather than a DB-driven source table - there is only one feed for now.
/// </summary>
public sealed class ShipmentFeedPoller(
    IServiceScopeFactory scopeFactory,
    IOptions<LtsOptions> options,
    ILogger<ShipmentFeedPoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.ShipmentFeed;

        if (!settings.Enabled)
        {
            logger.LogInformation("Shipment feed polling is disabled by configuration.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(10, settings.PollSeconds));
        logger.LogInformation("Shipment feed poller started; polling every {Interval}.", interval);

        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<ShipmentFeedRunner>();
                await runner.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Backstop only - ShipmentFeedRunner already isolates per-country failures.
                logger.LogError(exception, "Shipment feed poll cycle failed.");
            }
        }
    }
}
