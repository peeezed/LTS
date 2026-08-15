using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LTS.Application.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LTS.Infrastructure.Integration;

/// <summary>
/// Base for real country adapters that read JSON over HTTP. It handles the parts every such
/// adapter repeats — authentication from configuration, cursor query parameters, deserialisation
/// — so a new country's adapter only has to describe its own endpoints and payload shape.
/// </summary>
/// <remarks>
/// A concrete adapter overrides <see cref="FetchAsync"/> and calls <see cref="GetJsonAsync{T}"/>
/// for each endpoint it needs, then maps that system's payload onto the canonical DTOs.
/// </remarks>
public abstract class HttpJsonAdapter(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger logger) : ICountryIntegrationAdapter
{
    protected static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public abstract string AdapterKey { get; }

    public abstract Task<IntegrationFetchResult> FetchAsync(
        IntegrationContext context, CancellationToken cancellationToken = default);

    protected ILogger Logger { get; } = logger;

    /// <summary>
    /// Calls one endpoint relative to the source's base URL and deserialises the response.
    /// Returns default when the source replies 404, which sources commonly use for "nothing yet".
    /// </summary>
    protected async Task<T?> GetJsonAsync<T>(
        IntegrationContext context,
        string relativePath,
        IReadOnlyDictionary<string, string?>? query = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(context.BaseUrl))
        {
            throw new InvalidOperationException(
                $"Integration source {context.SourceId} has no base URL configured.");
        }

        using var client = httpClientFactory.CreateClient(AdapterKey);
        client.BaseAddress = new Uri(context.BaseUrl.TrimEnd('/') + "/");

        // Secrets stay in configuration; the database only ever holds the name of the entry.
        if (context.SecretName is { Length: > 0 } secretName)
        {
            var secret = configuration[$"Integration:Secrets:{secretName}"];

            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidOperationException(
                    $"Secret '{secretName}' is not configured under Integration:Secrets.");
            }

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        }

        var url = BuildUrl(relativePath, query);
        using var response = await client.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Logger.LogDebug("{Adapter} got 404 from {Url}; treating it as no data.", AdapterKey, url);
            return default;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
    }

    private static string BuildUrl(string relativePath, IReadOnlyDictionary<string, string?>? query)
    {
        if (query is null || query.Count == 0)
        {
            return relativePath;
        }

        var pairs = query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}");

        var queryString = string.Join("&", pairs);

        return queryString.Length == 0
            ? relativePath
            : $"{relativePath}{(relativePath.Contains('?') ? '&' : '?')}{queryString}";
    }
}
