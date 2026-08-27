using LTS.Application.Abstractions;
using LTS.Application.DelayAlerts;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LTS.Infrastructure.DelayAlerts;

/// <summary>
/// Builds and sends the two delay alert mails. RunDueAsync is the scheduled path (one poll tick,
/// every enabled config whose SendTime has passed for a day it hasn't already sent); SendNowAsync
/// is the admin page's manual "Send Now" button, which always sends (even an empty report) and
/// never touches LastSentDate, so it can't consume the day's scheduled slot.
/// </summary>
public sealed class DelayAlertRunner(
    IDbContextFactory<LtsIntegrationDbContext> dbFactory,
    IEmailSender emailSender,
    IClock clock,
    ILogger<DelayAlertRunner> logger)
{
    public async Task RunDueAsync(CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        var now = TimeOnly.FromTimeSpan(clock.UtcNow.TimeOfDay);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var due = await db.DelayAlertConfigs
            .Where(c => c.IsEnabled && c.LastSentDate != today && c.SendTime <= now)
            .ToListAsync(cancellationToken);

        foreach (var config in due)
        {
            try
            {
                var recipients = ParseRecipients(config.Recipients);

                if (recipients.Count == 0)
                {
                    logger.LogWarning(
                        "Delay alert: no recipients configured for {MailKind}/{CountryId}; skipping.",
                        config.MailKind, config.CountryId);
                }
                else
                {
                    var (rowCount, attachment, fileName) = await BuildReportAsync(db, config, today, cancellationToken);

                    if (rowCount == 0)
                    {
                        // Nothing delayed today - a deliberate default, no "all clear" email.
                        logger.LogInformation(
                            "Delay alert: nothing delayed for {MailKind}/{CountryId}; skipping today's send.",
                            config.MailKind, config.CountryId);
                    }
                    else
                    {
                        await SendMailAsync(config, recipients, attachment, fileName, cancellationToken);
                    }
                }

                // Consumes today's slot either way - a config that has nothing to report, or no
                // recipients, should not be re-checked every poll tick for the rest of the day.
                config.LastSentDate = today;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // LastSentDate deliberately left untouched - a transient failure (e.g. SMTP
                // unreachable) gets retried on the next poll tick, still the same day.
                logger.LogError(exception,
                    "Delay alert: failed to send {MailKind} report for country {CountryId}.",
                    config.MailKind, config.CountryId);
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task SendNowAsync(int countryId, DelayAlertMailKind mailKind, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var config = await db.DelayAlertConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CountryId == countryId && c.MailKind == mailKind, cancellationToken)
            ?? throw new InvalidOperationException("This delay alert is not configured yet.");

        if (!config.IsEnabled)
        {
            throw new InvalidOperationException("This delay alert is disabled.");
        }

        var recipients = ParseRecipients(config.Recipients);
        if (recipients.Count == 0)
        {
            throw new InvalidOperationException("No recipients are configured for this delay alert.");
        }

        var (_, attachment, fileName) = await BuildReportAsync(db, config, clock.Today, cancellationToken);
        await SendMailAsync(config, recipients, attachment, fileName, cancellationToken);
    }

    private static async Task<(int RowCount, byte[] Attachment, string FileName)> BuildReportAsync(
        LtsIntegrationDbContext db, LtsIntegrationDelayAlertConfig config, DateOnly today, CancellationToken cancellationToken)
    {
        if (config.MailKind == DelayAlertMailKind.Shipment)
        {
            var rows = await DelayAlertReportBuilder.BuildShipmentRowsAsync(db, config.CountryId, today, cancellationToken);
            return (rows.Count, DelayAlertExcelBuilder.BuildShipmentReport(rows), $"ShipmentDelayAlert_{today:yyyy-MM-dd}.xlsx");
        }

        var transferRows = await DelayAlertReportBuilder.BuildTransferRowsAsync(db, config.CountryId, today, cancellationToken);
        return (transferRows.Count, DelayAlertExcelBuilder.BuildTransferReport(transferRows), $"TransferDelayAlert_{today:yyyy-MM-dd}.xlsx");
    }

    private Task SendMailAsync(
        LtsIntegrationDelayAlertConfig config, IReadOnlyList<string> recipients, byte[] attachment, string fileName,
        CancellationToken cancellationToken) =>
        emailSender.SendAsync(
            new EmailMessage(
                recipients,
                config.Subject ?? $"{config.MailKind} Delay Alert",
                config.Body ?? "",
                [new EmailAttachment(fileName, attachment, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]),
            cancellationToken);

    private static IReadOnlyList<string> ParseRecipients(string? recipients) =>
        string.IsNullOrWhiteSpace(recipients)
            ? []
            : [.. recipients.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
