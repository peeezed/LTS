using LTS.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LTS.Infrastructure.RomaniaOneClick;

/// <summary>
/// Wakes up on a fixed tick (default hourly) and runs one full RomaniaShipmentFeedRunner pass.
/// Config-driven (Lts:RomaniaOneClick), same PeriodicTimer/BackgroundService skeleton as
/// ShipmentFeedPoller - see that class for the pattern this mirrors.
/// </summary>
public sealed class RomaniaShipmentPoller(
    IServiceScopeFactory scopeFactory,
    IOptions<LtsOptions> options,
    ILogger<RomaniaShipmentPoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.RomaniaOneClick;

        if (!settings.Enabled)
        {
            logger.LogInformation("Romania OneClick polling is disabled by configuration.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(10, settings.PollSeconds));
        logger.LogInformation("Romania OneClick poller started; polling every {Interval}.", interval);

        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<RomaniaShipmentFeedRunner>();
                await runner.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Backstop only - RomaniaShipmentFeedRunner already isolates per-transfer failures.
                logger.LogError(exception, "Romania OneClick poll cycle failed.");
            }
        }
    }
}
