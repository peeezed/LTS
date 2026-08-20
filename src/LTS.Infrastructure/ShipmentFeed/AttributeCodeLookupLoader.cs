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
        LtsIntegrationDbContext db, IReadOnlyList<RawShipmentFeedDto> records, CancellationToken cancellationToken) =>
        new(
            await ResolveAsync(db.ArrivalCustomsAttributes, records.Select(r => r.ArrivalCustoms), cancellationToken),
            await ResolveAsync(db.ExportTypeAttributes, records.Select(r => r.ExportType), cancellationToken),
            await ResolveAsync(db.TransportTypeAttributes, records.Select(r => r.TransportType), cancellationToken),
            await ResolveAsync(db.LoadingPointAttributes, records.Select(r => r.LoadingPoint), cancellationToken),
            await ResolveAsync(db.LogisticsCompanyAttributes, records.Select(r => r.LogisticsCompany), cancellationToken),
            await ResolveAsync(db.BrokerAttributes, records.Select(r => r.BrokerCompany), cancellationToken));

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
