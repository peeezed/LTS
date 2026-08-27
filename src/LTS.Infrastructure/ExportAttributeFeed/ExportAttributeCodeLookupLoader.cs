using LTS.Application.ExportAttributeFeed;
using LTS.Application.ShipmentFeed;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.ExportAttributeFeed;

/// <summary>
/// Builds AttributeCodeLookups by batch-querying the six attribute tables, Code-keyed, from a
/// batch of ExportFileDetailDto responses. Structurally mirrors ShipmentFeed's own
/// AttributeCodeLookupLoader, but deliberately not shared with it - a small duplicate of its
/// ResolveAsync helper, so this module's Infrastructure code has no dependency on ShipmentFeed's.
/// </summary>
internal static class ExportAttributeCodeLookupLoader
{
    public static async Task<AttributeCodeLookups> LoadAsync(
        LtsIntegrationDbContext db, IReadOnlyList<ExportFileDetailDto> entries, CancellationToken cancellationToken) =>
        new(
            await ResolveAsync(db.ArrivalCustomsAttributes, entries.Select(e => e.ArrivalCustoms), cancellationToken),
            await ResolveAsync(db.ExportTypeAttributes, entries.Select(e => e.ExportType), cancellationToken),
            await ResolveAsync(db.TransportTypeAttributes, entries.Select(e => e.Transport), cancellationToken),
            await ResolveAsync(db.LoadingPointAttributes, entries.Select(e => e.LoadingPoint), cancellationToken),
            // Carier maps onto the Logistics Company lookup table, BrokerCompany onto Broker -
            // same tables the Admin UI, filter dropdowns and ShipmentFeed already resolve against.
            await ResolveAsync(db.LogisticsCompanyAttributes, entries.Select(e => e.Carier), cancellationToken),
            await ResolveAsync(db.BrokerAttributes, entries.Select(e => e.BrokerCompany), cancellationToken));

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
