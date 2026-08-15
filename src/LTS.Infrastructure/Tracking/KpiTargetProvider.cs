using LTS.Application.Tracking;
using LTS.Domain.Kpi;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LTS.Infrastructure.Tracking;

/// <summary>
/// Loads the active KPI targets once and keeps them in memory. The list is small — a few
/// hundred rows — but it is consulted for every row of every grid, so re-querying it per
/// render would dominate the page cost.
/// </summary>
public sealed class KpiTargetProvider(LtsDbContext db, IMemoryCache cache) : IKpiTargetProvider
{
    private const string CacheKey = "kpi-targets";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public async Task<KpiTargetResolver> GetResolverAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out KpiTargetResolver? cached) && cached is not null)
        {
            return cached;
        }

        var targets = await db.KpiTargets
            .AsNoTracking()
            .Where(t => t.IsActive)
            .ToListAsync(cancellationToken);

        var resolver = new KpiTargetResolver(targets);
        cache.Set(CacheKey, resolver, CacheDuration);

        return resolver;
    }

    public void Invalidate() => cache.Remove(CacheKey);
}
