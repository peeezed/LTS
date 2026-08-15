using LTS.Domain.Enums;

namespace LTS.Application.Reference;

public sealed record CountryInput(int? Id, string Code, string Name, bool IsActive, bool UseWorkingDays);

public sealed record LookupInput(int? Id, LookupKind Kind, int? CountryId, string Code, string Name, int SortOrder, bool IsActive);

public sealed record PartnerInput(int? Id, PartnerType Type, string Code, string Name, bool IsActive);

public sealed record LoadingPointInput(int? Id, string Code, string Name, string CountryCode, bool IsActive);

public sealed record StoreInput(int? Id, int CountryId, string Code, string Name, bool IsActive);

/// <summary>
/// Maintains the reference data a country needs before shipments can be imported: its customs
/// offices, export and transport types, loading points, partners and stores. Onboarding a
/// country is filling these in, not writing code.
/// </summary>
public interface IMasterDataService
{
    Task<int> SaveCountryAsync(CountryInput input, CancellationToken cancellationToken = default);

    Task<int> SaveLookupAsync(LookupInput input, CancellationToken cancellationToken = default);

    Task<int> SavePartnerAsync(PartnerInput input, CancellationToken cancellationToken = default);

    Task<int> SaveLoadingPointAsync(LoadingPointInput input, CancellationToken cancellationToken = default);

    Task<int> SaveStoreAsync(StoreInput input, CancellationToken cancellationToken = default);
}
