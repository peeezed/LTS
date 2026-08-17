using LTS.Application.Reference;
using LTS.Domain.Entities;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Reference;

public sealed class MasterDataService(
    LtsDbContext db, IDbContextFactory<LtsIntegrationDbContext> integrationDbFactory) : IMasterDataService
{
    public async Task<int> SaveCountryAsync(CountryInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var code = Require(input.Code, "Country code").ToUpperInvariant();

        var country = await FindOrCreateAsync(db.Countries, input.Id,
            () => new Country { Code = code, Name = input.Name }, cancellationToken);

        country.Code = code;
        country.Name = Require(input.Name, "Country name");
        country.IsActive = input.IsActive;
        country.UseWorkingDays = input.UseWorkingDays;

        await db.SaveChangesAsync(cancellationToken);

        return country.Id;
    }

    public async Task<int> SaveIntegrationCountryAsync(
        IntegrationCountryInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var code = Require(input.Code, "Country code").ToUpperInvariant();

        await using var integrationDb = await integrationDbFactory.CreateDbContextAsync(cancellationToken);

        LtsIntegrationCountry country;
        if (input.Id is { } id)
        {
            var rawId = IntegrationCountryId.ToRawId(id);
            country = await integrationDb.Countries.FirstOrDefaultAsync(c => c.Id == rawId, cancellationToken)
                ?? throw new InvalidOperationException($"Country {rawId} does not exist.");
        }
        else
        {
            country = new LtsIntegrationCountry { CountryCode = code, CountryDescription = input.Name };
            integrationDb.Countries.Add(country);
        }

        country.CountryCode = code;
        country.CountryDescription = Require(input.Name, "Country name");
        country.IsActive = input.IsActive;
        country.CustomerCode = string.IsNullOrWhiteSpace(input.CustomerCode) ? null : input.CustomerCode.Trim();

        await integrationDb.SaveChangesAsync(cancellationToken);

        return IntegrationCountryId.ToAppId(country.Id);
    }

    public async Task<int> SaveLookupAsync(LookupInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var lookup = await FindOrCreateAsync(db.LookupValues, input.Id,
            () => new LookupValue { Kind = input.Kind, Code = input.Code, Name = input.Name }, cancellationToken);

        lookup.Kind = input.Kind;
        lookup.CountryId = input.CountryId;
        lookup.Code = Require(input.Code, "Code");
        lookup.Name = Require(input.Name, "Name");
        lookup.SortOrder = input.SortOrder;
        lookup.IsActive = input.IsActive;

        await db.SaveChangesAsync(cancellationToken);

        return lookup.Id;
    }

    public async Task<int> SavePartnerAsync(PartnerInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var partner = await FindOrCreateAsync(db.Partners, input.Id,
            () => new Partner { Type = input.Type, Code = input.Code, Name = input.Name }, cancellationToken);

        partner.Type = input.Type;
        partner.Code = Require(input.Code, "Code");
        partner.Name = Require(input.Name, "Name");
        partner.IsActive = input.IsActive;

        await db.SaveChangesAsync(cancellationToken);

        return partner.Id;
    }

    public async Task<int> SaveLoadingPointAsync(LoadingPointInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var point = await FindOrCreateAsync(db.LoadingPoints, input.Id,
            () => new LoadingPoint { Code = input.Code, Name = input.Name, CountryCode = input.CountryCode },
            cancellationToken);

        point.Code = Require(input.Code, "Code");
        point.Name = Require(input.Name, "Name");

        // The loading country is what KPI targets are matched on, so it is never left blank.
        point.CountryCode = Require(input.CountryCode, "Loading country code").ToUpperInvariant();
        point.IsActive = input.IsActive;

        await db.SaveChangesAsync(cancellationToken);

        return point.Id;
    }

    public async Task<int> SaveStoreAsync(StoreInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var store = await FindOrCreateAsync(db.Stores, input.Id,
            () => new Store { CountryId = input.CountryId, Code = input.Code, Name = input.Name }, cancellationToken);

        store.CountryId = input.CountryId;
        store.Code = Require(input.Code, "Code");
        store.Name = Require(input.Name, "Name");
        store.IsActive = input.IsActive;

        await db.SaveChangesAsync(cancellationToken);

        return store.Id;
    }

    private async Task<TEntity> FindOrCreateAsync<TEntity>(
        DbSet<TEntity> set,
        int? id,
        Func<TEntity> create,
        CancellationToken cancellationToken)
        where TEntity : Domain.Common.Entity
    {
        if (id is not { } existingId)
        {
            var created = create();
            set.Add(created);
            return created;
        }

        return await set.FirstOrDefaultAsync(e => e.Id == existingId, cancellationToken)
               ?? throw new InvalidOperationException($"{typeof(TEntity).Name} {existingId} does not exist.");
    }

    private static string Require(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{field} is required.")
            : value.Trim();
}
