namespace LTS.Application.Reference;

/// <summary>
/// An LTS_Integration country. Id is in the app-wide offset id space (see IntegrationCountryId);
/// there is no UseWorkingDays column on LTS_Countries yet. CustomerCode identifies the country to
/// the integration layer (matched against inbound source data), separate from the ISO Code.
/// </summary>
public sealed record IntegrationCountryInput(int? Id, string Code, string Name, bool IsActive, string? CustomerCode);

public sealed record StoreInput(
    int? Id, int CountryId, string Code, string? CurrAccCode, string Description, string? City, bool IsActive);

/// <summary>One row of one of LTS_Integration's Code+Description shipment attribute tables.</summary>
public sealed record AttributeInput(int? Id, string Code, string Description);

/// <summary>
/// Maintains LTS_Integration's own reference data: countries, the shared shipment attribute
/// lookup tables, and per-country stores. Onboarding a country is filling these in, not writing
/// code.
/// </summary>
public interface IMasterDataService
{
    Task<int> SaveIntegrationCountryAsync(IntegrationCountryInput input, CancellationToken cancellationToken = default);

    Task<int> SaveStoreAsync(StoreInput input, CancellationToken cancellationToken = default);

    Task<int> SaveIntegrationAttributeAsync(
        AttributeKind kind, AttributeInput input, CancellationToken cancellationToken = default);
}
