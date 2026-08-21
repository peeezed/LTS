using LTS.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LTS.Infrastructure.Tracking;

/// <summary>
/// Wakes up on a fixed tick and runs one ShipmentStatusReconciler pass. No external dependency
/// (no endpoint, no secret) unlike the other pollers, so this is enabled by default.
/// </summary>
public sealed class ShipmentStatusReconciliationPoller(
    IServiceScopeFactory scopeFactory,
    IOptions<LtsOptions> options,
    ILogger<ShipmentStatusReconciliationPoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.ShipmentStatusReconciliation;

        if (!settings.Enabled)
        {
            logger.LogInformation("Shipment status reconciliation is disabled by configuration.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(10, settings.PollSeconds));
        logger.LogInformation("Shipment status reconciliation started; running every {Interval}.", interval);

        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var reconciler = scope.ServiceProvider.GetRequiredService<ShipmentStatusReconciler>();
                await reconciler.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Shipment status reconciliation cycle failed.");
            }
        }
    }
}
