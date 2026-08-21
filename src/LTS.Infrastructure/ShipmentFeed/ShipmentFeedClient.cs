using System.Net.Http.Headers;
using System.Text.Json;
using LTS.Application.ShipmentFeed;
using LTS.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LTS.Infrastructure.ShipmentFeed;

/// <summary>Fetches shipment data from the company's own internal 1C-based shipments API.</summary>
public interface IShipmentFeedClient
{
    /// <summary>GetInvoiceListByCustomerCode - every shipment header + attribute codes for one country.</summary>
    Task<IReadOnlyList<InvoiceListEntryDto>> FetchInvoiceListAsync(
        string customerCode, CancellationToken cancellationToken = default);

    /// <summary>GetInvoiceDetailByInvoiceNumber - box/product/store lines for one shipment.</summary>
    Task<IReadOnlyList<InvoiceDetailLineDto>> FetchInvoiceDetailAsync(
        string invoiceNumber, CancellationToken cancellationToken = default);
}

/// <summary>
/// Mirrors the old (dead) HttpJsonAdapter's HTTP/auth handling (base address from config, bearer
/// token from Integration:Secrets:{SecretName}, case-insensitive JSON) as a standalone class -
/// there's only one feed here, so no adapter-registry indirection. The shared spec documents both
/// endpoints as wrapping their payload in { IsSuccess, Value, Message }, but it isn't confirmed
/// the live API actually does that for every call rather than just returning the bare array - so
/// GetAsync below accepts either shape rather than assuming one.
/// </summary>
public sealed class ShipmentFeedClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IOptions<LtsOptions> options,
    ILogger<ShipmentFeedClient> logger) : IShipmentFeedClient
{
    private const string ClientName = "ShipmentFeed";

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public Task<IReadOnlyList<InvoiceListEntryDto>> FetchInvoiceListAsync(
        string customerCode, CancellationToken cancellationToken = default) =>
        GetAsync<InvoiceListEntryDto>(
            $"InvoiceListClass/GetInvoiceListByCustomerCode?customerCode={Uri.EscapeDataString(customerCode)}",
            cancellationToken);

    public Task<IReadOnlyList<InvoiceDetailLineDto>> FetchInvoiceDetailAsync(
        string invoiceNumber, CancellationToken cancellationToken = default) =>
        GetAsync<InvoiceDetailLineDto>(
            $"InvoiceDetail/GetInvoiceDetailByInvoiceNumber?invoiceNumber={Uri.EscapeDataString(invoiceNumber)}",
            cancellationToken);

    private async Task<IReadOnlyList<T>> GetAsync<T>(string relativePath, CancellationToken cancellationToken)
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
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(raw))
        {
            logger.LogWarning("Shipment feed got an empty response from {Path}.", relativePath);
            return [];
        }

        using var document = JsonDocument.Parse(raw);

        // Bare array: the response is just the list, no envelope.
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return document.RootElement.Deserialize<List<T>>(SerializerOptions) ?? [];
        }

        // Otherwise, the documented { IsSuccess, Value, Message } envelope.
        var envelope = document.RootElement.Deserialize<ApiEnvelope<List<T>>>(SerializerOptions);

        if (envelope is null)
        {
            logger.LogWarning("Shipment feed got an empty response from {Path}.", relativePath);
            return [];
        }

        if (!envelope.IsSuccess)
        {
            throw new InvalidOperationException($"Shipment feed call to {relativePath} failed: {envelope.Message}");
        }

        return envelope.Value ?? [];
    }
}
