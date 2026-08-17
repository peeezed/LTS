using LTS.Application.Security;
using LTS.Domain.Enums;

namespace LTS.Application.Reference;

public sealed record CountryDto(int Id, string Code, string Name, bool IsActive, string? CustomerCode = null)
{
    public string Display => $"{Code} - {Name}";
}

public sealed record LookupDto(int Id, string Code, string Name, LookupKind Kind, int? CountryId, int SortOrder, bool IsActive);

public sealed record PartnerDto(int Id, string Code, string Name, PartnerType Type, bool IsActive);

public sealed record LoadingPointDto(int Id, string Code, string Name, string CountryCode, bool IsActive)
{
    public string Display => $"{Name} ({CountryCode})";
}

public sealed record StoreDto(int Id, string Code, string Name, int CountryId, bool IsActive)
{
    public string Display => $"{Code} - {Name}";
}

/// <summary>
/// Reads the shared lists that fill the filter dropdowns, the admin master-data pages and the
/// Excel import validators.
/// </summary>
public interface IReferenceDataService
{
    Task<IReadOnlyList<CountryDto>> GetCountriesAsync(bool activeOnly = true, CancellationToken cancellationToken = default);

    /// <summary>Countries the user may enter — the list offered right after login.</summary>
    Task<IReadOnlyList<CountryDto>> GetAccessibleCountriesAsync(
        UserPermissions permissions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Countries from LTS_Integration's own country table, read directly from that database
    /// rather than the app's. Temporary: until permissions and country-scoping are migrated
    /// too, this is unfiltered by access - every signed-in user sees every active row.
    /// </summary>
    Task<IReadOnlyList<CountryDto>> GetIntegrationCountriesAsync(
        bool activeOnly = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves one country by code from LTS_Integration, for route resolution. Counterpart to
    /// <see cref="GetIntegrationCountriesAsync"/> - same temporary caveat about permissions.
    /// </summary>
    Task<CountryDto?> GetIntegrationCountryByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<CountryDto?> GetCountryByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Values of one attribute list. Country-specific values are returned alongside the global
    /// ones, so a country sees its own customs offices plus the shared export types.
    /// </summary>
    Task<IReadOnlyList<LookupDto>> GetLookupsAsync(
        LookupKind kind, int? countryId = null, bool activeOnly = true, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PartnerDto>> GetPartnersAsync(
        PartnerType? type = null, bool activeOnly = true, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LoadingPointDto>> GetLoadingPointsAsync(
        bool activeOnly = true, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoreDto>> GetStoresAsync(
        int countryId, bool activeOnly = true, CancellationToken cancellationToken = default);
}
