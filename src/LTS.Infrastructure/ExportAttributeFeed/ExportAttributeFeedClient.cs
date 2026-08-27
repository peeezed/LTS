using System.Net.Http.Headers;
using System.Text.Json;
using LTS.Application.ExportAttributeFeed;
using LTS.Application.ShipmentFeed;
using LTS.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LTS.Infrastructure.ExportAttributeFeed;

/// <summary>Fetches one shipment's export attributes from the company's own internal 1C-based API.</summary>
public interface IExportAttributeFeedClient
{
    /// <summary>GetLTSExportFileDetail - the export attributes for one shipment, or null if none was returned.</summary>
    Task<ExportFileDetailDto?> FetchExportFileDetailAsync(
        string exportFileNumber, CancellationToken cancellationToken = default);
}

/// <summary>
/// Mirrors ShipmentFeedClient's HTTP/auth handling (base address + bearer token from
/// Integration:Secrets:{SecretName}, case-insensitive JSON, tolerant of either a bare array or the
/// documented {IsSuccess, Value, Message} envelope) as its own standalone class, reading its own
/// Lts:ExportAttributeFeed settings rather than Lts:ShipmentFeed.
/// </summary>
public sealed class ExportAttributeFeedClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IOptions<LtsOptions> options,
    ILogger<ExportAttributeFeedClient> logger) : IExportAttributeFeedClient
{
    private const string ClientName = "ExportAttributeFeed";

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<ExportFileDetailDto?> FetchExportFileDetailAsync(
        string exportFileNumber, CancellationToken cancellationToken = default)
    {
        var settings = options.Value.ExportAttributeFeed;

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new InvalidOperationException("Lts:ExportAttributeFeed:BaseUrl is not configured.");
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

        var relativePath = $"LTS/GetLTSExportFileDetail?exportFileNumber={Uri.EscapeDataString(exportFileNumber)}";

        using var response = await client.GetAsync(relativePath, cancellationToken);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(raw))
        {
            logger.LogWarning("Export attribute feed got an empty response for '{ExportFileNumber}'.", exportFileNumber);
            return null;
        }

        using var document = JsonDocument.Parse(raw);

        List<ExportFileDetailDto>? entries;

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            // Bare array: the response is just the list, no envelope.
            entries = document.RootElement.Deserialize<List<ExportFileDetailDto>>(SerializerOptions);
        }
        else
        {
            // Otherwise, the documented { IsSuccess, Value, Message } envelope.
            var envelope = document.RootElement.Deserialize<ApiEnvelope<List<ExportFileDetailDto>>>(SerializerOptions);

            if (envelope is null)
            {
                logger.LogWarning("Export attribute feed got an empty response for '{ExportFileNumber}'.", exportFileNumber);
                return null;
            }

            if (!envelope.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Export attribute feed call for '{exportFileNumber}' failed: {envelope.Message}");
            }

            entries = envelope.Value;
        }

        return entries?.FirstOrDefault();
    }
}
