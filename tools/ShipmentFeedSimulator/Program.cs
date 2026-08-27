using System.Text.Json;
using LTS.Application.Abstractions;
using LTS.Application.ExportAttributeFeed;
using LTS.Application.ShipmentFeed;
using LTS.Application.Tracking;
using LTS.Infrastructure.ExportAttributeFeed;
using LTS.Infrastructure.Persistence;
using LTS.Infrastructure.ShipmentFeed;
using LTS.Infrastructure.Tracking;
using Microsoft.EntityFrameworkCore;
using ShipmentFeedSimulator;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("LtsIntegration")
    ?? throw new InvalidOperationException("Connection string 'LtsIntegration' is not configured.");

builder.Services.AddDbContextFactory<LtsIntegrationDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddSingleton<IClock, SystemClock>();
// ShipmentFeedRunner's constructor takes IShipmentFeedClient, but SimulateAsync (the only method
// this tool calls) never touches it - real HTTP calls are replaced by what you paste into the
// page instead. This stub exists only to satisfy DI.
builder.Services.AddSingleton<IShipmentFeedClient, UnusedShipmentFeedClient>();
builder.Services.AddScoped<ShipmentFeedRunner>();

// Same reasoning for ExportAttributeFeedRunner.SimulateAsync and IExportAttributeFeedClient.
builder.Services.AddSingleton<IExportAttributeFeedClient, UnusedExportAttributeFeedClient>();
builder.Services.AddScoped<IIntegrationMilestoneService, IntegrationMilestoneService>();
builder.Services.AddScoped<ExportAttributeFeedRunner>();

var app = builder.Build();

var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

// Accepts either shape - a bare array, or the documented {IsSuccess, Value, Message} envelope -
// since it isn't confirmed yet which one the real API actually returns. Whatever you paste here,
// this figures out which one it is rather than requiring you to strip a wrapper first.
List<T> ParseEntries<T>(string json)
{
    if (string.IsNullOrWhiteSpace(json))
    {
        return [];
    }

    using var document = JsonDocument.Parse(json);

    return document.RootElement.ValueKind == JsonValueKind.Array
        ? document.RootElement.Deserialize<List<T>>(jsonOptions) ?? []
        : document.RootElement.Deserialize<ApiEnvelope<List<T>>>(jsonOptions)?.Value ?? [];
}

app.MapGet("/", () => Results.Content(Page.Html, "text/html"));

app.MapGet("/export-attributes", () => Results.Content(ExportAttributePage.Html, "text/html"));

// Step 1: parse the GetInvoiceListByCustomerCode response on its own, independent of any detail
// call - mirrors the real flow, where this call happens first and by itself. Returns just enough
// of each entry to populate the shipment picker; no DB access here.
app.MapPost("/parse-list", (ParseListRequest request) =>
{
    List<InvoiceListEntryDto> entries;

    try
    {
        entries = ParseEntries<InvoiceListEntryDto>(request.ListJson);
    }
    catch (JsonException exception)
    {
        return Results.BadRequest(new { error = $"Could not parse the List JSON: {exception.Message}" });
    }

    if (entries.Count == 0)
    {
        return Results.BadRequest(new { error = "The List JSON has no entries." });
    }

    var summary = entries.Select((e, index) => new
    {
        index,
        invoiceNumber = e.InvoiceNumber,
        exportNumber = e.ExportNumber,
        invoiceDate = e.InvoiceDate
    });

    return Results.Ok(new { entries = summary });
});

// Step 2: fetch (paste) the GetInvoiceDetailByInvoiceNumber response for whichever shipment was
// picked from step 1's list, and run it through the real standardize+upsert path. The list JSON
// is re-sent rather than cached server-side - this tool has no session state, only what's on the
// page - and re-parsing it here is cheap and keeps the two calls genuinely independent.
app.MapPost("/simulate", async (SimulateRequest request, ShipmentFeedRunner runner) =>
{
    if (string.IsNullOrWhiteSpace(request.CustomerCode))
    {
        return Results.BadRequest(new { error = "Customer code is required." });
    }

    List<InvoiceListEntryDto> entries;

    try
    {
        entries = ParseEntries<InvoiceListEntryDto>(request.ListJson);
    }
    catch (JsonException exception)
    {
        return Results.BadRequest(new { error = $"Could not parse the List JSON: {exception.Message}" });
    }

    if (request.SelectedIndex < 0 || request.SelectedIndex >= entries.Count)
    {
        return Results.BadRequest(new { error = "Selected shipment is out of range - reload the list and pick again." });
    }

    var header = entries[request.SelectedIndex];

    List<InvoiceDetailLineDto> detailLines;

    try
    {
        detailLines = ParseEntries<InvoiceDetailLineDto>(request.DetailJson);
    }
    catch (JsonException exception)
    {
        return Results.BadRequest(new { error = $"Could not parse the Detail JSON: {exception.Message}" });
    }

    try
    {
        var fields = await runner.SimulateAsync(request.CustomerCode, header, detailLines);
        return Results.Ok(fields);
    }
    catch (Exception exception)
    {
        return Results.Problem(exception.Message);
    }
});

// Runs one GetLTSExportFileDetail response (a bare array, or a single object pasted directly)
// through the real standardize+apply+recompute path. Independent of the /simulate route above -
// its own request shape, its own runner, no shared state.
app.MapPost("/simulate-export-attributes", async (ExportAttributeSimulateRequest request, ExportAttributeFeedRunner runner) =>
{
    List<ExportFileDetailDto> entries;

    try
    {
        entries = ParseEntries<ExportFileDetailDto>(request.DetailJson);
    }
    catch (JsonException exception)
    {
        return Results.BadRequest(new { error = $"Could not parse the JSON: {exception.Message}" });
    }

    if (entries.Count == 0)
    {
        return Results.BadRequest(new { error = "No entries found in the pasted JSON." });
    }

    try
    {
        var fields = await runner.SimulateAsync(entries[0]);

        return fields is null
            ? Results.BadRequest(new { error = $"No shipment found with reference number '{entries[0].ExportFileNumber}'." })
            : Results.Ok(fields);
    }
    catch (Exception exception)
    {
        return Results.Problem(exception.Message);
    }
});

app.Run("http://localhost:5299");

internal sealed record ParseListRequest(string ListJson);

internal sealed record SimulateRequest(string CustomerCode, string ListJson, int SelectedIndex, string DetailJson);

internal sealed record ExportAttributeSimulateRequest(string DetailJson);

/// <summary>Never called - see the registration comment above.</summary>
internal sealed class UnusedShipmentFeedClient : IShipmentFeedClient
{
    public Task<IReadOnlyList<InvoiceListEntryDto>> FetchInvoiceListAsync(string customerCode, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The simulator never makes real HTTP calls.");

    public Task<IReadOnlyList<InvoiceDetailLineDto>> FetchInvoiceDetailAsync(string invoiceNumber, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The simulator never makes real HTTP calls.");
}

/// <summary>Never called - see the registration comment above.</summary>
internal sealed class UnusedExportAttributeFeedClient : IExportAttributeFeedClient
{
    public Task<ExportFileDetailDto?> FetchExportFileDetailAsync(string exportFileNumber, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The simulator never makes real HTTP calls.");
}
