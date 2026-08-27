using LTS.Application.ShipmentFeed;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.ShipmentFeed;

/// <summary>
/// Builds AttributeCodeLookups by batch-querying the six attribute tables, Code-keyed - the exact
/// mirror of IntegrationShipmentQueryService.ResolveOneAsync, which is Description-keyed for the
/// opposite (read-time display) direction. Costs 6 queries total per country per poll, not 6
/// times the number of shipments.
/// </summary>
internal static class AttributeCodeLookupLoader
{
    public static async Task<AttributeCodeLookups> LoadAsync(
        LtsIntegrationDbContext db, IReadOnlyList<InvoiceListEntryDto> entries, CancellationToken cancellationToken) =>
        new(
            await ResolveAsync(db.ArrivalCustomsAttributes, entries.Select(e => e.Arrival_Customs), cancellationToken),
            await ResolveAsync(db.ExportTypeAttributes, entries.Select(e => e.Export_Type), cancellationToken),
            await ResolveAsync(db.TransportTypeAttributes, entries.Select(e => e.Transport), cancellationToken),
            await ResolveAsync(db.LoadingPointAttributes, entries.Select(e => e.Loading_Point), cancellationToken),
            // Carier maps onto the Logistics Company lookup table, Broker_Company onto Broker -
            // same tables the Admin UI and filter dropdowns already resolve against.
            await ResolveAsync(db.LogisticsCompanyAttributes, entries.Select(e => e.Carier), cancellationToken),
            await ResolveAsync(db.BrokerAttributes, entries.Select(e => e.Broker_Company), cancellationToken));

    private static async Task<Dictionary<string, string>> ResolveAsync(
        IQueryable<LtsIntegrationAttribute> table, IEnumerable<string?> rawCodes, CancellationToken cancellationToken)
    {
        var codes = rawCodes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!).Distinct().ToList();
        if (codes.Count == 0)
        {
            return [];
        }

        var matches = await table.AsNoTracking().Where(a => codes.Contains(a.Code)).ToListAsync(cancellationToken);
        return matches.ToDictionary(a => a.Code, a => a.Description);
    }
}
