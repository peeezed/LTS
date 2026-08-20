using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LTS.Application.ShipmentFeed;
using LTS.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LTS.Infrastructure.ShipmentFeed;

/// <summary>Fetches shipment data from the company's own internal shipments API.</summary>
public interface IShipmentFeedClient
{
    /// <summary>Lists shipment references visible for one country. Empty (not null) when there's nothing new.</summary>
    Task<IReadOnlyList<ShipmentReferenceDto>> FetchShipmentReferencesAsync(
        string customerCode, CancellationToken cancellationToken = default);

    /// <summary>TBD endpoint owning core header fields. Null when the source has nothing for this reference.</summary>
    Task<ShipmentHeaderDetailDto?> FetchShipmentHeaderAsync(
        string referenceNo, CancellationToken cancellationToken = default);

    /// <summary>TBD endpoint owning the six shipment attribute codes.</summary>
    Task<ShipmentAttributesDetailDto?> FetchShipmentAttributesAsync(
        string referenceNo, CancellationToken cancellationToken = default);

    /// <summary>TBD endpoint owning box/item/transfer counts.</summary>
    Task<ShipmentCountsDetailDto?> FetchShipmentCountsAsync(
        string referenceNo, CancellationToken cancellationToken = default);
}

/// <summary>
/// Mirrors the old (dead) HttpJsonAdapter's HTTP/auth handling (base address from config, bearer
/// token from Integration:Secrets:{SecretName}, 404 treated as "no data", case-insensitive JSON)
/// as a standalone class - there's only one feed here, so no adapter-registry indirection.
///
/// TBD: the relative paths/query parameters below, and whether it's really 3 or 4 detail calls,
/// are placeholders until the real endpoint contract is supplied - isolated to this one class so
/// nothing else in the pipeline needs to change when they are.
/// </summary>
public sealed class ShipmentFeedClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IOptions<LtsOptions> options,
    ILogger<ShipmentFeedClient> logger) : IShipmentFeedClient
{
    private const string ClientName = "ShipmentFeed";

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public Task<IReadOnlyList<ShipmentReferenceDto>> FetchShipmentReferencesAsync(
        string customerCode, CancellationToken cancellationToken = default) =>
        GetListAsync<ShipmentReferenceDto>(
            $"shipments?customerCode={Uri.EscapeDataString(customerCode)}", cancellationToken);

    public Task<ShipmentHeaderDetailDto?> FetchShipmentHeaderAsync(
        string referenceNo, CancellationToken cancellationToken = default) =>
        GetAsync<ShipmentHeaderDetailDto>($"shipments/{Uri.EscapeDataString(referenceNo)}/header", cancellationToken);

    public Task<ShipmentAttributesDetailDto?> FetchShipmentAttributesAsync(
        string referenceNo, CancellationToken cancellationToken = default) =>
        GetAsync<ShipmentAttributesDetailDto>(
            $"shipments/{Uri.EscapeDataString(referenceNo)}/attributes", cancellationToken);

    public Task<ShipmentCountsDetailDto?> FetchShipmentCountsAsync(
        string referenceNo, CancellationToken cancellationToken = default) =>
        GetAsync<ShipmentCountsDetailDto>($"shipments/{Uri.EscapeDataString(referenceNo)}/counts", cancellationToken);

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        var result = await GetAsync<List<T>>(relativePath, cancellationToken);
        return result ?? [];
    }

    private async Task<T?> GetAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        var settings = options.Value.ShipmentFeed;

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new InvalidOperationException("Lts:ShipmentFeed:BaseUrl is not configured.");
        }

        using var client = httpClientFactory.CreateClient(ClientName);
        client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");

        // Secrets stay in configuration; the database never holds anything but the source name.
        if (settings.SecretName is { Length: > 0 } secretName)
        {
            var secret = configuration[$"Integration:Secrets:{secretName}"];

            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidOperationException(
                    $"Secret '{secretName}' is not configured under Integration:Secrets.");
            }

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        }

        using var response = await client.GetAsync(relativePath, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogDebug("Shipment feed got 404 from {Path}; treating it as no data.", relativePath);
            return default;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
    }
}
