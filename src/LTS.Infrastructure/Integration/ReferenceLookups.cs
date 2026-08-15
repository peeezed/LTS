using LTS.Domain.Entities;
using LTS.Domain.Enums;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Integration;

/// <summary>
/// Code-to-id lookups for one integration run. External systems speak in codes, so every
/// imported shipment needs the same handful of translations; loading them once per run keeps a
/// thousand-shipment import to a handful of queries.
/// </summary>
internal sealed class ReferenceLookups
{
    private readonly Dictionary<string, int> _countries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(LookupKind, string), int> _lookups = [];
    private readonly Dictionary<string, LoadingPoint> _loadingPoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, LoadingPoint> _loadingPointsById = [];
    private readonly Dictionary<(PartnerType, string), int> _partners = [];
    private readonly Dictionary<(int, string), int> _stores = [];

    public static async Task<ReferenceLookups> LoadAsync(LtsDbContext db, CancellationToken cancellationToken)
    {
        var lookups = new ReferenceLookups();

        foreach (var country in await db.Countries.AsNoTracking().ToListAsync(cancellationToken))
        {
            lookups._countries[country.Code] = country.Id;
        }

        foreach (var value in await db.LookupValues.AsNoTracking().Where(l => l.IsActive).ToListAsync(cancellationToken))
        {
            // Country-specific values do not collide across countries in practice, and the last
            // one wins if they ever do, which is no worse than an arbitrary choice.
            lookups._lookups[(value.Kind, value.Code.ToUpperInvariant())] = value.Id;
        }

        foreach (var point in await db.LoadingPoints.AsNoTracking().ToListAsync(cancellationToken))
        {
            lookups._loadingPoints[point.Code] = point;
            lookups._loadingPointsById[point.Id] = point;
        }

        foreach (var partner in await db.Partners.AsNoTracking().ToListAsync(cancellationToken))
        {
            lookups._partners[(partner.Type, partner.Code.ToUpperInvariant())] = partner.Id;
        }

        foreach (var store in await db.Stores.AsNoTracking().ToListAsync(cancellationToken))
        {
            lookups._stores[(store.CountryId, store.Code.ToUpperInvariant())] = store.Id;
        }

        return lookups;
    }

    public int? CountryId(string? code) =>
        code is not null && _countries.TryGetValue(code, out var id) ? id : null;

    public int? LookupId(LookupKind kind, string? code) =>
        code is not null && _lookups.TryGetValue((kind, code.ToUpperInvariant()), out var id) ? id : null;

    public int? LoadingPointId(string? code) =>
        code is not null && _loadingPoints.TryGetValue(code, out var point) ? point.Id : null;

    /// <summary>
    /// The loading point entity, which KPI matching needs for its country code before the
    /// shipment has been saved and its navigation loaded.
    /// </summary>
    public LoadingPoint? LoadingPoint(int id) => _loadingPointsById.GetValueOrDefault(id);

    public int? PartnerId(PartnerType type, string? code) =>
        code is not null && _partners.TryGetValue((type, code.ToUpperInvariant()), out var id) ? id : null;

    public int? StoreId(int countryId, string? code) =>
        code is not null && _stores.TryGetValue((countryId, code.ToUpperInvariant()), out var id) ? id : null;
}
