using LTS.Application.Security;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LTS.Infrastructure.Security;

/// <summary>
/// Builds a user's <see cref="UserPermissions"/> from the country and page grant tables.
/// </summary>
public sealed class PermissionService(LtsDbContext db, IMemoryCache cache) : IPermissionService
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

        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => new { u.Id, u.UserType, u.PartnerId })
            .FirstOrDefaultAsync(cancellationToken);

        // A deactivated or deleted account keeps its cookie until it expires, so it has to
        // resolve to no permissions rather than to its old ones.
        if (user is null)
        {
            return UserPermissions.None;
        }

        var countries = await db.UserCountryAccess
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.Country!.IsActive)
            .Select(a => a.CountryId)
            .ToListAsync(cancellationToken);

        var grants = await db.UserPagePermissions
            .AsNoTracking()
            .Where(p => p.UserId == userId && (p.CanView || p.CanEdit))
            .Select(p => new { p.CountryId, p.PageKey, p.CanView, p.CanEdit })
            .ToListAsync(cancellationToken);

        var pages = grants.ToDictionary(
            g => UserPermissions.Key(g.PageKey, g.CountryId),
            g => new PagePermission(g.CanView, g.CanEdit),
            StringComparer.OrdinalIgnoreCase);

        var permissions = new UserPermissions(user.Id, user.UserType, user.PartnerId, countries, pages);
        cache.Set(key, permissions, CacheDuration);

        return permissions;
    }

    public void Invalidate(Guid userId) => cache.Remove(CacheKey(userId));

    private static string CacheKey(Guid userId) => $"permissions:{userId}";
}
