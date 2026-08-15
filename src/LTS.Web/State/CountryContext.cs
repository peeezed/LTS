using LTS.Application.Reference;

namespace LTS.Web.State;

/// <summary>
/// The country the user is currently working in. The country code is part of every page's
/// route, so a link or bookmark carries it; this holds the resolved country for the circuit and
/// enforces that the user is actually allowed into it.
/// </summary>
public sealed class CountryContext(IReferenceDataService reference, PermissionState permissions)
{
    /// <summary>Raised when the country changes, so open pages can reload their data.</summary>
    public event Action? Changed;

    public CountryDto? Current { get; private set; }

    public bool HasCountry => Current is not null;

    public int CountryId => Current?.Id ?? 0;

    public string CountryCode => Current?.Code ?? string.Empty;

    /// <summary>
    /// Resolves a country code from the route. Returns false when the code is unknown or the
    /// user has no access, which the layout turns into a redirect to the country chooser.
    /// </summary>
    public async Task<bool> TrySetAsync(string? code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        if (string.Equals(Current?.Code, code, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var country = await reference.GetCountryByCodeAsync(code, cancellationToken);
        if (country is null || !country.IsActive)
        {
            return false;
        }

        var userPermissions = await permissions.GetAsync(cancellationToken);
        if (!userPermissions.HasCountry(country.Id))
        {
            return false;
        }

        Current = country;
        Changed?.Invoke();

        return true;
    }

    public void Clear()
    {
        Current = null;
        Changed?.Invoke();
    }
}
