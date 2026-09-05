using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LTS.Application.RomaniaOneClick;
using LTS.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LTS.Infrastructure.RomaniaOneClick;

/// <summary>Looks up one KLG OneClick domestic shipment by its perm_shipment_id.</summary>
public interface IRomaniaOneClickClient
{
    /// <summary>Null if KLG has no shipment matching this id. Throws RomaniaRateLimitedException on 429.</summary>
    Task<RomaniaDomesticShipmentDto?> GetShipmentByPermIdAsync(
        string permShipmentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Calls GET /api/v1/domestic-shipments filtered by perm_shipment_id - one call per LTS transfer,
/// never a bulk list (see RomaniaShipmentFeedRunner). filter[perm_shipment_id] is documented as a
/// "starts with" match, not exact, so a page of up to 25 candidates is requested and matched
/// client-side against the exact id - taking whatever KLG returned first without checking could
/// silently apply a different shipment's dates onto this transfer.
/// </summary>
public sealed class RomaniaOneClickClient(
    IHttpClientFactory httpClientFactory,
    IRomaniaTokenStore tokenStore,
    IOptions<LtsOptions> options,
    ILogger<RomaniaOneClickClient> logger) : IRomaniaOneClickClient
{
    private const string ClientName = "RomaniaOneClick";

    // "Starts with" matching means a short id could have several candidates sharing that prefix;
    // 25 is a generous margin over what a real perm_shipment_id collision should ever produce.
    private const int CandidatePageSize = 25;

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<RomaniaDomesticShipmentDto?> GetShipmentByPermIdAsync(
        string permShipmentId, CancellationToken cancellationToken = default)
    {
        var settings = options.Value.RomaniaOneClick;

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new InvalidOperationException("Lts:RomaniaOneClick:BaseUrl is not configured.");
        }

        var relativePath =
            "api/v1/domestic-shipments"
            + $"?filter[perm_shipment_id]={Uri.EscapeDataString(permShipmentId)}"
            + $"&page[number]=1&page[size]={CandidatePageSize}"
            + "&fields[domestic-shipments]=perm_shipment_id,shipment_status,shipment_date,loading_act_start_date,unloading_start_date,unloading_act_start_date";

        var accessToken = await tokenStore.GetValidAccessTokenAsync(cancellationToken);
        var response = await SendAsync(settings.BaseUrl, relativePath, accessToken, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // The token store refreshes proactively, so a 401 here means the stored token was
            // invalidated some other way (e.g. someone regenerated a pair by hand in OneClick's
            // UI) - refresh once and retry once, rather than retrying indefinitely.
            logger.LogWarning("Romania OneClick: got 401 for perm_shipment_id '{PermShipmentId}', forcing a token refresh and retrying once.", permShipmentId);
            var refreshedToken = await tokenStore.RefreshAsync(cancellationToken);
            response = await SendAsync(settings.BaseUrl, relativePath, refreshedToken, cancellationToken);
        }

        if (response.StatusCode == (HttpStatusCode)429)
        {
            throw new RomaniaRateLimitedException(
                $"Romania OneClick rate-limited the lookup for perm_shipment_id '{permShipmentId}'.");
        }

        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var envelope = JsonSerializer.Deserialize<OneClickListResponseDto>(raw, SerializerOptions);
        var match = envelope?.Data?
            .Select(r => r.Attributes)
            .FirstOrDefault(a => a is not null && string.Equals(a.PermShipmentId, permShipmentId, StringComparison.Ordinal));

        if (match is null)
        {
            return null;
        }

        return new RomaniaDomesticShipmentDto(
            PermShipmentId: match.PermShipmentId!,
            ShipmentStatus: match.ShipmentStatus,
            ShipmentDate: ParseDate(match.ShipmentDate, permShipmentId, nameof(match.ShipmentDate)),
            LoadingActStartDate: ParseDate(match.LoadingActStartDate, permShipmentId, nameof(match.LoadingActStartDate)),
            UnloadingStartDate: ParseDate(match.UnloadingStartDate, permShipmentId, nameof(match.UnloadingStartDate)),
            UnloadingActStartDate: ParseDate(match.UnloadingActStartDate, permShipmentId, nameof(match.UnloadingActStartDate)));
    }

    /// <summary>Delegates to the pure RomaniaOneClickDateParser, logging when a non-blank value fails to parse.</summary>
    private DateOnly? ParseDate(string? value, string permShipmentId, string fieldName)
    {
        var parsed = RomaniaOneClickDateParser.Parse(value);

        if (parsed is null && !string.IsNullOrWhiteSpace(value))
        {
            logger.LogWarning(
                "Romania OneClick: could not parse '{FieldName}' value '{Value}' for perm_shipment_id '{PermShipmentId}'.",
                fieldName, value, permShipmentId);
        }

        return parsed;
    }

    private async Task<HttpResponseMessage> SendAsync(
        string baseUrl, string relativePath, string accessToken, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(ClientName);
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await client.GetAsync(relativePath, cancellationToken);
    }
}
