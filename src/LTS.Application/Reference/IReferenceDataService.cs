namespace LTS.Application.Reference;

public sealed record CountryDto(int Id, string Code, string Name, bool IsActive, string? CustomerCode = null)
{
    public string Display => $"{Code} - {Name}";
}

/// <summary>
/// Code/Description are nullable: a store the shipment feed created as a failsafe (see
/// ShipmentFeedRunner.EnsureStoreAsync) only has a CurrAccCode until an admin fills the rest in.
/// </summary>
public sealed record StoreDto(
    int Id, string? Code, string? CurrAccCode, string? Description, string? City, int CountryId, bool IsActive)
{
    public string Display => Code is null ? $"Unmapped ({CurrAccCode})" : $"{Code} - {Description}";
}

/// <summary>Which of LTS_Integration's shipment attribute lookup tables to read or write.</summary>
public enum AttributeKind { ArrivalCustoms, ExportType, TransportType, LoadingPoint, LogisticsCompany, Broker }

/// <summary>One row of one of LTS_Integration's Code+Description shipment attribute tables.</summary>
public sealed record AttributeDto(int Id, string Code, string Description)
{
    public string Display => $"{Code} - {Description}";
}

/// <summary>
/// Reads the shared lists that fill the filter dropdowns, the admin master-data pages and the
/// Excel import validators.
/// </summary>
public interface IReferenceDataService
{
    /// <summary>
    /// Countries from LTS_Integration's own country table. Temporary: until permissions and
    /// country-scoping are migrated too, this is unfiltered by access - every signed-in user
    /// sees every active row.
    /// </summary>
    Task<IReadOnlyList<CountryDto>> GetIntegrationCountriesAsync(
        bool activeOnly = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves one country by code from LTS_Integration, for route resolution. Counterpart to
    /// <see cref="GetIntegrationCountriesAsync"/> - same temporary caveat about permissions.
    /// </summary>
    Task<CountryDto?> GetIntegrationCountryByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Stores from LTS_Integration's own store table, scoped to one country.</summary>
    Task<IReadOnlyList<StoreDto>> GetStoresAsync(
        int countryId, bool activeOnly = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// One of LTS_Integration's seven shipment attribute lookup tables (everything but Arrival
    /// Country, which has no lookup table of its own - see IntegrationShipmentQueryService).
    /// </summary>
    Task<IReadOnlyList<AttributeDto>> GetIntegrationAttributesAsync(
        AttributeKind kind, CancellationToken cancellationToken = default);
}
