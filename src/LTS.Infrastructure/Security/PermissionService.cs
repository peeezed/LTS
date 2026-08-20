using LTS.Application.Security;
using LTS.Infrastructure.Persistence;
using LTS.Infrastructure.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LTS.Infrastructure.Security;

/// <summary>
/// Builds a user's <see cref="UserPermissions"/> from the country and page grant tables.
/// </summary>
public sealed class PermissionService(
    IDbContextFactory<LtsIntegrationDbContext> dbFactory, IMemoryCache cache) : IPermissionService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<UserPermissions> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return UserPermissions.None;
        }

        var key = CacheKey(userId);
        if (cache.TryGetValue(key, out UserPermissions? cached) && cached is not null)
        {
            return cached;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => new { u.Id, u.UserType, u.PartnerId, u.SupplierCompanyCode })
            .FirstOrDefaultAsync(cancellationToken);

        // A deactivated or deleted account keeps its cookie until it expires, so it has to
        // resolve to no permissions rather than to its old ones.
        if (user is null)
        {
            return UserPermissions.None;
        }

        // CountryId here is LTS_Integration's own raw id; the rest of the app compares against
        // the offset id everywhere (see IntegrationCountryId), so it is converted on the way out.
        var countries = await db.UserCountryAccess
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => a.CountryId + IntegrationCountryId.Offset)
            .ToListAsync(cancellationToken);

        var grants = await db.UserPagePermissions
            .AsNoTracking()
            .Where(p => p.UserId == userId && (p.CanView || p.CanEdit))
            .Select(p => new { CountryId = p.CountryId + IntegrationCountryId.Offset, p.PageKey, p.CanView, p.CanEdit })
            .ToListAsync(cancellationToken);

        var pages = grants.ToDictionary(
            g => UserPermissions.Key(g.PageKey, g.CountryId),
            g => new PagePermission(g.CanView, g.CanEdit),
            StringComparer.OrdinalIgnoreCase);

        var permissions = new UserPermissions(
            user.Id, user.UserType, user.PartnerId, user.SupplierCompanyCode, countries, pages);
        cache.Set(key, permissions, CacheDuration);

        return permissions;
    }

    public void Invalidate(Guid userId) => cache.Remove(CacheKey(userId));

    private static string CacheKey(Guid userId) => $"permissions:{userId}";
}
