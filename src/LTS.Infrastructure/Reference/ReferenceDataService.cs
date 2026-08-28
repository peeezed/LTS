using LTS.Application.Reference;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Reference;

public sealed class ReferenceDataService(
    IDbContextFactory<LtsIntegrationDbContext> integrationDbFactory) : IReferenceDataService
{
    public async Task<IReadOnlyList<CountryDto>> GetIntegrationCountriesAsync(
        bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        await using var integrationDb = await integrationDbFactory.CreateDbContextAsync(cancellationToken);

        return await integrationDb.Countries
            .AsNoTracking()
            .Where(c => !activeOnly || c.IsActive)
            .OrderBy(c => c.CountryDescription)
            .Select(c => new CountryDto(c.Id + IntegrationCountryId.Offset, c.CountryCode, c.CountryDescription, c.IsActive, c.CustomerCode))
            .ToListAsync(cancellationToken);
    }

    public async Task<CountryDto?> GetIntegrationCountryByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var normalised = code.Trim().ToUpperInvariant();

        await using var integrationDb = await integrationDbFactory.CreateDbContextAsync(cancellationToken);

        return await integrationDb.Countries
            .AsNoTracking()
            .Where(c => c.CountryCode == normalised)
            .Select(c => new CountryDto(c.Id + IntegrationCountryId.Offset, c.CountryCode, c.CountryDescription, c.IsActive, c.CustomerCode))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoreDto>> GetStoresAsync(
        int countryId, bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        var rawCountryId = IntegrationCountryId.ToRawId(countryId);

        await using var integrationDb = await integrationDbFactory.CreateDbContextAsync(cancellationToken);

        return await integrationDb.Stores
            .AsNoTracking()
            .Where(s => s.CountryId == rawCountryId)
            .Where(s => !activeOnly || s.IsActive)
            .OrderBy(s => s.Code)
            .Select(s => new StoreDto(s.Id, s.Code, s.CurrAccCode, s.Description, s.City, countryId, s.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AttributeDto>> GetIntegrationAttributesAsync(
        AttributeKind kind, CancellationToken cancellationToken = default)
    {
        await using var integrationDb = await integrationDbFactory.CreateDbContextAsync(cancellationToken);

        return await AttributeTables.For(integrationDb, kind)
            .AsNoTracking()
            .OrderBy(a => a.Description)
            .Select(a => new AttributeDto(a.Id, a.Code, a.Description))
            .ToListAsync(cancellationToken);
    }
}
