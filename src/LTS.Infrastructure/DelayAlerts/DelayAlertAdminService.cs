using LTS.Application.DelayAlerts;
using LTS.Infrastructure.Persistence;
using LTS.Infrastructure.Reference;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.DelayAlerts;

/// <summary>
/// CRUD over LTS_DelayAlertConfigs, always scoped to one country + mail kind. countryId parameters
/// are the app-wide offset id (see IDelayAlertAdminService); converted to LTS_Integration's own raw
/// id before touching LTS_DelayAlertConfigs.CountryId, the same convention IntegrationKpiAdminService
/// already follows.
/// </summary>
public sealed class DelayAlertAdminService(
    IDbContextFactory<LtsIntegrationDbContext> dbFactory, DelayAlertRunner runner) : IDelayAlertAdminService
{
    public async Task<DelayAlertConfigRow> GetConfigAsync(
        int countryId, DelayAlertMailKind mailKind, CancellationToken cancellationToken = default)
    {
        var rawCountryId = IntegrationCountryId.ToRawId(countryId);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var config = await db.DelayAlertConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CountryId == rawCountryId && c.MailKind == mailKind, cancellationToken);

        return config is null
            ? new DelayAlertConfigRow { SendTime = new TimeOnly(8, 0) }
            : Map(config);
    }

    public async Task SaveAsync(
        int countryId, DelayAlertMailKind mailKind, DelayAlertConfigInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var rawCountryId = IntegrationCountryId.ToRawId(countryId);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var config = await db.DelayAlertConfigs
            .FirstOrDefaultAsync(c => c.CountryId == rawCountryId && c.MailKind == mailKind, cancellationToken);

        if (config is null)
        {
            config = new LtsIntegrationDelayAlertConfig { CountryId = rawCountryId, MailKind = mailKind };
            db.DelayAlertConfigs.Add(config);
        }

        config.IsEnabled = input.IsEnabled;
        config.Recipients = Trim(input.Recipients);
        config.SendTime = input.SendTime;
        config.Subject = Trim(input.Subject);
        config.Body = Trim(input.Body);

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task SendNowAsync(int countryId, DelayAlertMailKind mailKind, CancellationToken cancellationToken = default) =>
        runner.SendNowAsync(IntegrationCountryId.ToRawId(countryId), mailKind, cancellationToken);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DelayAlertConfigRow Map(LtsIntegrationDelayAlertConfig config) => new()
    {
        IsEnabled = config.IsEnabled,
        Recipients = config.Recipients,
        SendTime = config.SendTime,
        Subject = config.Subject,
        Body = config.Body,
        LastSentDate = config.LastSentDate
    };
}
