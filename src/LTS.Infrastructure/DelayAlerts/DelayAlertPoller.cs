using LTS.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LTS.Infrastructure.DelayAlerts;

/// <summary>
/// Wakes up on a short, fixed tick (Lts:DelayAlerts:CheckIntervalSeconds, not each config's own
/// once-a-day SendTime) and asks DelayAlertRunner which per-country configs are due right now.
/// Structurally identical to the other pollers in this app.
/// </summary>
public sealed class DelayAlertPoller(
    IServiceScopeFactory scopeFactory,
    IOptions<LtsOptions> options,
    ILogger<DelayAlertPoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.DelayAlerts;

        if (!settings.Enabled)
        {
            logger.LogInformation("Delay alert polling is disabled by configuration.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(10, settings.CheckIntervalSeconds));
        logger.LogInformation("Delay alert poller started; checking every {Interval}.", interval);

        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<DelayAlertRunner>();
                await runner.RunDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Backstop only - DelayAlertRunner already isolates per-config failures.
                logger.LogError(exception, "Delay alert poll cycle failed.");
            }
        }
    }
}
