using LTS.Application.Security;
using LTS.Web.State;
using Microsoft.AspNetCore.Components;

namespace LTS.Web.Components.Shared;

/// <summary>
/// Base for every page that lives under a country route. It resolves the country from the URL,
/// checks that the user may enter it and may view the page, and only then lets the page load
/// its data — so no page has to remember to do those three things itself.
/// </summary>
public abstract class CountryPageBase : ComponentBase
{
    private string? _loadedFor;

    [Parameter]
    public string? CountryCode { get; set; }

    [Inject] protected CountryContext Country { get; set; } = default!;
    [Inject] protected PermissionState PermissionState { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;

    /// <summary>The page this component represents, from <see cref="LTS.Domain.Security.PageKeys"/>.</summary>
    protected abstract string PageKey { get; }

    protected UserPermissions Permissions { get; private set; } = UserPermissions.None;

    /// <summary>True once the country and permissions have been resolved and the page may render.</summary>
    protected bool Ready { get; private set; }

    protected int CountryId => Country.CountryId;

    protected bool CanEdit => Permissions.CanEdit(PageKey, CountryId);

    protected override async Task OnParametersSetAsync()
    {
        Permissions = await PermissionState.GetAsync();

        // An unknown country code, or one the user has no access to, goes back to the chooser
        // rather than showing an empty page that looks like missing data.
        if (!await Country.TrySetAsync(CountryCode))
        {
            Navigation.NavigateTo("/", replace: true);
            return;
        }

        if (!Permissions.CanView(PageKey, CountryId))
        {
            Navigation.NavigateTo("/access-denied", replace: true);
            return;
        }

        Ready = true;

        // Parameters are set again on every navigation within the same page, so data is only
        // reloaded when the country actually changed.
        if (_loadedFor == Country.CountryCode)
        {
            return;
        }

        _loadedFor = Country.CountryCode;
        await OnCountryReadyAsync();
    }

    /// <summary>Loads the page's data. Called once per country.</summary>
    protected virtual Task OnCountryReadyAsync() => Task.CompletedTask;
}
