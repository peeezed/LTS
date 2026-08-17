using LTS.Application.Reference;
using LTS.Application.Security;
using LTS.Domain.Enums;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Reference;

public sealed class ReferenceDataService(LtsDbContext db, LtsIntegrationDbContext integrationDb) : IReferenceDataService
{
    /// <summary>
    /// Added to LTS_Integration's own small sequential row ids before they are exposed as a
    /// CountryDto.Id. Without this, e.g. LTS_Integration's Romania (id 1) and the app's own
    /// Turkey (id 1) are indistinguishable to any query that filters on a bare int country id -
    /// which is exactly what happened before this was added: opening Romania showed Turkey's
    /// real shipments, because both ids were 1. Far outside the range either table's identity
    /// column will ever reach, so the two id spaces can never collide.
    /// </summary>
    private const int IntegrationCountryIdOffset = 1_000_000;

    public async Task<IReadOnlyList<CountryDto>> GetCountriesAsync(
        bool activeOnly = true, CancellationToken cancellationToken = default) =>
        await db.Countries
            .AsNoTracking()
            .Where(c => !activeOnly || c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new CountryDto(c.Id, c.Code, c.Name, c.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CountryDto>> GetAccessibleCountriesAsync(
        UserPermissions permissions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var query = db.Countries.AsNoTracking().Where(c => c.IsActive);

        // Admins are deliberately not listed in the grant tables, so they get every country.
        if (!permissions.IsAdmin)
        {
            var allowed = permissions.CountryIds.ToList();
            query = query.Where(c => allowed.Contains(c.Id));
        }

        return await query
            .OrderBy(c => c.Name)
            .Select(c => new CountryDto(c.Id, c.Code, c.Name, c.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CountryDto>> GetIntegrationCountriesAsync(CancellationToken cancellationToken = default) =>
        await integrationDb.Countries
            .AsNoTracking()
            .OrderBy(c => c.CountryDescription)
            .Select(c => new CountryDto(c.Id + IntegrationCountryIdOffset, c.CountryCode, c.CountryDescription, true))
            .ToListAsync(cancellationToken);

    public async Task<CountryDto?> GetIntegrationCountryByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var normalised = code.Trim().ToUpperInvariant();

        return await integrationDb.Countries
            .AsNoTracking()
            .Where(c => c.CountryCode == normalised)
            .Select(c => new CountryDto(c.Id + IntegrationCountryIdOffset, c.CountryCode, c.CountryDescription, true))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<CountryDto?> GetCountryByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var normalised = code.Trim().ToUpperInvariant();

        return await db.Countries
            .AsNoTracking()
            .Where(c => c.Code == normalised)
            .Select(c => new CountryDto(c.Id, c.Code, c.Name, c.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LookupDto>> GetLookupsAsync(
        LookupKind kind, int? countryId = null, bool activeOnly = true, CancellationToken cancellationToken = default) =>
        await db.LookupValues
            .AsNoTracking()
            .Where(l => l.Kind == kind)
            .Where(l => !activeOnly || l.IsActive)
            // Global values apply everywhere; country-specific ones only where they belong.
            .Where(l => countryId == null || l.CountryId == null || l.CountryId == countryId)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Name)
            .Select(l => new LookupDto(l.Id, l.Code, l.Name, l.Kind, l.CountryId, l.SortOrder, l.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PartnerDto>> GetPartnersAsync(
        PartnerType? type = null, bool activeOnly = true, CancellationToken cancellationToken = default) =>
        await db.Partners
            .AsNoTracking()
            .Where(p => type == null || p.Type == type)
            .Where(p => !activeOnly || p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new PartnerDto(p.Id, p.Code, p.Name, p.Type, p.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LoadingPointDto>> GetLoadingPointsAsync(
        bool activeOnly = true, CancellationToken cancellationToken = default) =>
        await db.LoadingPoints
            .AsNoTracking()
            .Where(l => !activeOnly || l.IsActive)
            .OrderBy(l => l.CountryCode).ThenBy(l => l.Name)
            .Select(l => new LoadingPointDto(l.Id, l.Code, l.Name, l.CountryCode, l.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StoreDto>> GetStoresAsync(
        int countryId, bool activeOnly = true, CancellationToken cancellationToken = default) =>
        await db.Stores
            .AsNoTracking()
            .Where(s => s.CountryId == countryId)
            .Where(s => !activeOnly || s.IsActive)
            .OrderBy(s => s.Code)
            .Select(s => new StoreDto(s.Id, s.Code, s.Name, s.CountryId, s.IsActive))
            .ToListAsync(cancellationToken);
}
