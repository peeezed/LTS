using System.Net.Http.Headers;
using System.Net.Http.Json;
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
/// there's only one feed here, so no adapter-registry indirection. Both endpoints wrap their
/// payload in { IsSuccess, Value, Message }; IsSuccess = false throws with Message so the
/// runner's per-shipment try/catch can log it and move on.
///
/// TBD: the exact query-string parameter names below (customerCode/invoiceNumber) - the shared
/// spec gave response shapes, not the full request signature. Confirm against the real
/// API/Swagger; nothing else in the pipeline needs to change if these turn out different.
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

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<T>>>(SerializerOptions, cancellationToken);

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
