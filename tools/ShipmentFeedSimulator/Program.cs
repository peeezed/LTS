using System.Text.Json;
using LTS.Application.Abstractions;
using LTS.Application.ShipmentFeed;
using LTS.Infrastructure.Persistence;
using LTS.Infrastructure.ShipmentFeed;
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

var app = builder.Build();

var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

app.MapGet("/", () => Results.Content(Page.Html, "text/html"));

app.MapPost("/simulate", async (SimulateRequest request, ShipmentFeedRunner runner) =>
{
    if (string.IsNullOrWhiteSpace(request.CustomerCode))
    {
        return Results.BadRequest(new { error = "Customer code is required." });
    }

    ApiEnvelope<List<InvoiceListEntryDto>>? listEnvelope;
    ApiEnvelope<List<InvoiceDetailLineDto>>? detailEnvelope;

    try
    {
        listEnvelope = JsonSerializer.Deserialize<ApiEnvelope<List<InvoiceListEntryDto>>>(request.ListJson, jsonOptions);
    }
    catch (JsonException exception)
    {
        return Results.BadRequest(new { error = $"Could not parse the List JSON: {exception.Message}" });
    }

    try
    {
        detailEnvelope = JsonSerializer.Deserialize<ApiEnvelope<List<InvoiceDetailLineDto>>>(request.DetailJson, jsonOptions);
    }
    catch (JsonException exception)
    {
        return Results.BadRequest(new { error = $"Could not parse the Detail JSON: {exception.Message}" });
    }

    var header = listEnvelope?.Value?.FirstOrDefault();

    if (header is null)
    {
        return Results.BadRequest(new { error = "The List JSON has no entries under \"Value\"." });
    }

    var detailLines = (IReadOnlyList<InvoiceDetailLineDto>?)detailEnvelope?.Value ?? [];

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

app.Run("http://localhost:5299");

internal sealed record SimulateRequest(string CustomerCode, string ListJson, string DetailJson);

/// <summary>Never called - see the registration comment above.</summary>
internal sealed class UnusedShipmentFeedClient : IShipmentFeedClient
{
    public Task<IReadOnlyList<InvoiceListEntryDto>> FetchInvoiceListAsync(string customerCode, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The simulator never makes real HTTP calls.");

    public Task<IReadOnlyList<InvoiceDetailLineDto>> FetchInvoiceDetailAsync(string invoiceNumber, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The simulator never makes real HTTP calls.");
}
