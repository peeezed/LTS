using System.Text.Json;
using LTS.Application.Abstractions;
using LTS.Application.Integration;
using LTS.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LTS.Infrastructure.Integration;

/// <summary>
/// Reads canonical payloads from JSON files on disk instead of calling a real system. It lets
/// the whole integration path — poll, map, apply, audit, monitor — be exercised and demonstrated
/// before any country's API access exists, and gives each new adapter a known-good comparison.
/// </summary>
/// <remarks>
/// Files live under the configured mock data folder, named <c>{countryCode}.json</c> with a
/// <c>shipments</c> array and an <c>events</c> array. Events dated relative to today keep the
/// sample data fresh: an <c>eventDate</c> of <c>"-1"</c> means yesterday.
/// </remarks>
public sealed class MockJsonAdapter(
    IHostEnvironment environment,
    IOptions<LtsOptions> options,
    IClock clock,
    ILogger<MockJsonAdapter> logger) : ICountryIntegrationAdapter
{
    public const string Key = "mock-json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string AdapterKey => Key;

    public async Task<IntegrationFetchResult> FetchAsync(
        IntegrationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var path = Path.Combine(
            environment.ContentRootPath,
            options.Value.Integration.MockDataPath,
            $"{context.CountryCode}.json");

        if (!File.Exists(path))
        {
            logger.LogDebug("No mock payload at {Path}; nothing to import for {Country}.", path, context.CountryCode);
            return IntegrationFetchResult.Empty;
        }

        await using var stream = File.OpenRead(path);
        var payload = await JsonSerializer.DeserializeAsync<MockPayload>(stream, SerializerOptions, cancellationToken);

        if (payload is null)
        {
            return IntegrationFetchResult.Empty;
        }

        var events = payload.Events
            .Select(e => new MilestoneEventDto
            {
                Reference = e.Reference,
                RawStatusCode = e.RawStatusCode,
                EventDate = ResolveDate(e.EventDate),
                ExternalId = e.ExternalId,
                RawPayload = JsonSerializer.Serialize(e, SerializerOptions)
            })
            .ToList();

        return new IntegrationFetchResult(payload.Shipments, events, Cursor: clock.UtcNow.ToString("O"));
    }

    /// <summary>
    /// Accepts an absolute date or an offset in days from today, so a sample file does not go
    /// stale and start producing dates years in the past.
    /// </summary>
    private DateOnly ResolveDate(string value)
    {
        if (DateOnly.TryParse(value, out var absolute))
        {
            return absolute;
        }

        return int.TryParse(value, out var offset) ? clock.Today.AddDays(offset) : clock.Today;
    }

    private sealed record MockPayload
    {
        public List<ShipmentSnapshotDto> Shipments { get; init; } = [];
        public List<MockEvent> Events { get; init; } = [];
    }

    private sealed record MockEvent
    {
        public string Reference { get; init; } = string.Empty;
        public string RawStatusCode { get; init; } = string.Empty;

        /// <summary>Absolute date, or a day offset from today such as "-2".</summary>
        public string EventDate { get; init; } = "0";

        public string? ExternalId { get; init; }
    }
}
