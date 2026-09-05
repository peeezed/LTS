using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LTS.Application.Abstractions;
using LTS.Infrastructure.Configuration;
using LTS.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LTS.Infrastructure.RomaniaOneClick;

public interface IRomaniaTokenStore
{
    /// <summary>
    /// The access token to send as the Bearer header for a KLG call. Refreshes proactively if the
    /// stored one is missing or within an hour of expiring, so a normal caller almost never hits a
    /// 401 in the first place.
    /// </summary>
    Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>Forces a refresh regardless of the stored token's remaining lifetime - used after a 401.</summary>
    Task<string> RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Persists KLG's access/refresh token pair encrypted (via IDataProtector) in
/// LTS_RomaniaOneClickToken, since LTS_Integration is a shared database, not app-private. KLG
/// invalidates the whole pair on every refresh and reissues both values, so this always overwrites
/// the single stored row rather than appending history - losing the latest refresh token (a crash,
/// a skipped write) means a human has to regenerate a pair by hand in OneClick's UI, since the one
/// this app was last holding is already dead by the time a new one would be requested.
///
/// Every refresh - proactive or forced after a 401 - is staged into the same LTS_ShipmentFeedStaging
/// table the internal shipment feed already uses, but the raw HTTP body is never staged verbatim:
/// it is the live token pair, and staging it would defeat the point of encrypting the stored copy.
/// Only a redacted summary (status code, expires_in) is ever written there.
/// </summary>
public sealed class RomaniaTokenStore(
    IDbContextFactory<LtsIntegrationDbContext> dbFactory,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IOptions<LtsOptions> options,
    IDataProtectionProvider dataProtectionProvider,
    IClock clock,
    ILogger<RomaniaTokenStore> logger) : IRomaniaTokenStore
{
    private const string ClientName = "RomaniaOneClick";

    /// <summary>
    /// How long before expiry a still-valid access token is refreshed early, so a normal poll
    /// cycle (default hourly) essentially never has to react to an actual 401.
    /// </summary>
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromHours(1);

    /// <summary>
    /// KLG's refresh response never states the new refresh token's own lifetime - only the access
    /// token's, via expires_in. The docs are explicit that every rotated refresh token is fixed at
    /// 30 days regardless of what was configured (1 day - 2 years) at manual generation, so that's
    /// what is recorded here - informational only; this store does not currently act on it, since
    /// proactive access-token refresh alone keeps rotating it well inside that window.
    /// </summary>
    private static readonly TimeSpan RotatedRefreshTokenLifetime = TimeSpan.FromDays(30);

    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector("RomaniaOneClick.Tokens");

    // Serializes every read-check-refresh sequence so two near-simultaneous callers (e.g. a 401
    // retry racing the poller's own proactive check) never both call refreshToken at once - KLG
    // invalidates the previous pair on every call, so the loser of that race would be refreshing
    // with an already-dead refresh token.
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var row = await db.RomaniaOneClickTokens.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

            if (row is null)
            {
                return await BootstrapAsync(cancellationToken);
            }

            if (row.AccessTokenExpiresAtUtc <= clock.UtcNow.Add(RefreshBuffer))
            {
                return await RefreshWithAsync(protector.Unprotect(row.EncryptedRefreshToken), cancellationToken);
            }

            return protector.Unprotect(row.EncryptedAccessToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var row = await db.RomaniaOneClickTokens.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

            return row is null
                ? await BootstrapAsync(cancellationToken)
                : await RefreshWithAsync(protector.Unprotect(row.EncryptedRefreshToken), cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// First run ever: no persisted pair exists yet, so the initial refresh token from
    /// configuration is exchanged immediately for a verified pair with a real expires_in, rather
    /// than trusting the manually-generated access token's assumed 24-hour lifetime blindly - see
    /// RomaniaOneClickOptions.ApiKeySecretName's doc comment for why that secret is never read here.
    /// </summary>
    private async Task<string> BootstrapAsync(CancellationToken cancellationToken)
    {
        var secretName = options.Value.RomaniaOneClick.RefreshKeySecretName;

        if (string.IsNullOrWhiteSpace(secretName))
        {
            throw new InvalidOperationException("Lts:RomaniaOneClick:RefreshKeySecretName is not configured.");
        }

        var seedRefreshToken = configuration[$"Integration:Secrets:{secretName}"];

        if (string.IsNullOrWhiteSpace(seedRefreshToken))
        {
            throw new InvalidOperationException($"Secret '{secretName}' is not configured under Integration:Secrets.");
        }

        return await RefreshWithAsync(seedRefreshToken, cancellationToken);
    }

    private async Task<string> RefreshWithAsync(string currentRefreshToken, CancellationToken cancellationToken)
    {
        var settings = options.Value.RomaniaOneClick;

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new InvalidOperationException("Lts:RomaniaOneClick:BaseUrl is not configured.");
        }

        using var client = httpClientFactory.CreateClient(ClientName);
        client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));

        var requestBody = new OneClickRefreshTokenRequestDto(
            new OneClickRefreshTokenRequestDataDto(
                new OneClickRefreshTokenRequestAttributesDto(currentRefreshToken)));

        var fetchedAt = clock.UtcNow;
        HttpResponseMessage response;

        try
        {
            response = await client.PostAsJsonAsync("api/v1/tokens/-actions/refreshToken", requestBody, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await StageAsync(fetchedAt, "no response received", ShipmentFeedStagingStatus.Failed, exception.Message, cancellationToken);
            throw;
        }

        // The raw body is never staged from here on - a success body is the live token pair, and
        // even a failure body is not worth the risk of accidentally echoing a token fragment.
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            await StageAsync(fetchedAt, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                ShipmentFeedStagingStatus.Failed, $"refreshToken call failed with HTTP {(int)response.StatusCode}.", cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        var parsed = JsonSerializer.Deserialize<OneClickRefreshTokenResponseDto>(raw,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (parsed?.AccessToken is not { Length: > 0 } accessToken || parsed.RefreshToken is not { Length: > 0 } newRefreshToken)
        {
            await StageAsync(fetchedAt, "response parsed but access_token/refresh_token missing",
                ShipmentFeedStagingStatus.Failed, "refreshToken response was missing access_token/refresh_token.", cancellationToken);
            throw new InvalidOperationException("Romania OneClick refreshToken response was missing access_token/refresh_token.");
        }

        var accessExpiresAt = fetchedAt.AddSeconds(Math.Max(0, parsed.ExpiresIn));
        var refreshExpiresAt = fetchedAt.Add(RotatedRefreshTokenLifetime);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.RomaniaOneClickTokens.FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            row = new LtsIntegrationRomaniaOneClickToken
            {
                EncryptedAccessToken = protector.Protect(accessToken),
                EncryptedRefreshToken = protector.Protect(newRefreshToken),
                AccessTokenExpiresAtUtc = accessExpiresAt,
                RefreshTokenExpiresAtUtc = refreshExpiresAt,
                UpdatedAtUtc = fetchedAt
            };
            db.RomaniaOneClickTokens.Add(row);
        }
        else
        {
            row.EncryptedAccessToken = protector.Protect(accessToken);
            row.EncryptedRefreshToken = protector.Protect(newRefreshToken);
            row.AccessTokenExpiresAtUtc = accessExpiresAt;
            row.RefreshTokenExpiresAtUtc = refreshExpiresAt;
            row.UpdatedAtUtc = fetchedAt;
        }

        db.ShipmentFeedStaging.Add(new LtsShipmentFeedStagingRecord
        {
            CustomerCode = RomaniaConstants.CustomerCodeSentinel,
            CountryCode = "RO",
            EndpointKind = RomaniaOneClickEndpointKinds.TokenRefresh,
            FetchedAt = fetchedAt,
            ReferenceNo = null,
            RawPayload = $"{{\"token_type\":\"{parsed.TokenType}\",\"expires_in\":{parsed.ExpiresIn}}}",
            Status = ShipmentFeedStagingStatus.Processed,
            ProcessedAt = fetchedAt
        });

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Romania OneClick: token refreshed, access token valid until {ExpiresAt:u}.", accessExpiresAt);

        return accessToken;
    }

    private async Task StageAsync(
        DateTime fetchedAt, string redactedPayload, ShipmentFeedStagingStatus status, string errorMessage,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.ShipmentFeedStaging.Add(new LtsShipmentFeedStagingRecord
        {
            CustomerCode = RomaniaConstants.CustomerCodeSentinel,
            CountryCode = "RO",
            EndpointKind = RomaniaOneClickEndpointKinds.TokenRefresh,
            FetchedAt = fetchedAt,
            ReferenceNo = null,
            RawPayload = redactedPayload,
            Status = status,
            ProcessedAt = clock.UtcNow,
            ErrorMessage = errorMessage
        });

        await db.SaveChangesAsync(cancellationToken);
        logger.LogWarning("Romania OneClick: token refresh failed - {Error}", errorMessage);
    }
}
