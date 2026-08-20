using LTS.Domain.Enums;

namespace LTS.Application.Reference;

public sealed record CountryInput(int? Id, string Code, string Name, bool IsActive, bool UseWorkingDays);

/// <summary>
/// An LTS_Integration country. Id is in the app-wide offset id space (see IntegrationCountryId);
/// there is no UseWorkingDays column on LTS_Countries yet. CustomerCode identifies the country to
/// the integration layer (matched against inbound source data), separate from the ISO Code.
/// </summary>
public sealed record IntegrationCountryInput(int? Id, string Code, string Name, bool IsActive, string? CustomerCode);

public sealed record LookupInput(int? Id, LookupKind Kind, int? CountryId, string Code, string Name, int SortOrder, bool IsActive);

public sealed record PartnerInput(int? Id, PartnerType Type, string Code, string Name, bool IsActive);

public sealed record LoadingPointInput(int? Id, string Code, string Name, string CountryCode, bool IsActive);

public sealed record StoreInput(int? Id, int CountryId, string Code, string Name, bool IsActive);

/// <summary>One row of one of LTS_Integration's Code+Description shipment attribute tables.</summary>
public sealed record AttributeInput(int? Id, string Code, string Description);

/// <summary>
/// Maintains the reference data a country needs before shipments can be imported: its customs
/// offices, export and transport types, loading points, partners and stores. Onboarding a
/// country is filling these in, not writing code.
/// </summary>
public interface IMasterDataService
{
    Task<int> SaveCountryAsync(CountryInput input, CancellationToken cancellationToken = default);

    Task<int> SaveIntegrationCountryAsync(IntegrationCountryInput input, CancellationToken cancellationToken = default);

    Task<int> SaveLookupAsync(LookupInput input, CancellationToken cancellationToken = default);

    Task<int> SavePartnerAsync(PartnerInput input, CancellationToken cancellationToken = default);

    Task<int> SaveLoadingPointAsync(LoadingPointInput input, CancellationToken cancellationToken = default);

    Task<int> SaveStoreAsync(StoreInput input, CancellationToken cancellationToken = default);

    Task<int> SaveIntegrationAttributeAsync(
        AttributeKind kind, AttributeInput input, CancellationToken cancellationToken = default);
}
