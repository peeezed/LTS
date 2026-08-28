using LTS.Application.Reference;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Reference;

public sealed class MasterDataService(
    IDbContextFactory<LtsIntegrationDbContext> integrationDbFactory) : IMasterDataService
{
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

    public async Task<int> SaveStoreAsync(StoreInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var rawCountryId = IntegrationCountryId.ToRawId(input.CountryId);

        await using var integrationDb = await integrationDbFactory.CreateDbContextAsync(cancellationToken);

        LtsIntegrationStore store;
        if (input.Id is { } id)
        {
            store = await integrationDb.Stores.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new InvalidOperationException($"Store {id} does not exist.");
        }
        else
        {
            store = new LtsIntegrationStore { CountryId = rawCountryId, Code = input.Code, Description = input.Description };
            integrationDb.Stores.Add(store);
        }

        store.CountryId = rawCountryId;
        store.Code = Require(input.Code, "Code");
        store.Description = Require(input.Description, "Description");
        store.CurrAccCode = string.IsNullOrWhiteSpace(input.CurrAccCode) ? null : input.CurrAccCode.Trim();
        store.City = string.IsNullOrWhiteSpace(input.City) ? null : input.City.Trim();
        store.IsActive = input.IsActive;

        await integrationDb.SaveChangesAsync(cancellationToken);

        return store.Id;
    }

    public async Task<int> SaveIntegrationAttributeAsync(
        AttributeKind kind, AttributeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var code = Require(input.Code, "Code").ToUpperInvariant();

        await using var integrationDb = await integrationDbFactory.CreateDbContextAsync(cancellationToken);
        var table = AttributeTables.For(integrationDb, kind);

        LtsIntegrationAttribute attribute;
        if (input.Id is { } id)
        {
            attribute = await table.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                ?? throw new InvalidOperationException($"Attribute {id} does not exist.");
        }
        else
        {
            attribute = new LtsIntegrationAttribute { Code = code, Description = input.Description };
            table.Add(attribute);
        }

        attribute.Code = code;
        attribute.Description = Require(input.Description, "Description");

        await integrationDb.SaveChangesAsync(cancellationToken);

        return attribute.Id;
    }

    private static string Require(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{field} is required.")
            : value.Trim();
}
