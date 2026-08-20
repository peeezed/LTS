using LTS.Domain.Enums;
using LTS.Domain.Milestones;
using LTS.Domain.Security;

namespace LTS.Application.Security;

/// <summary>What a user may do on one page of one country.</summary>
public readonly record struct PagePermission(bool CanView, bool CanEdit)
{
    public static readonly PagePermission None = new(false, false);
    public static readonly PagePermission Full = new(true, true);
}

/// <summary>
/// The complete authorisation picture for one account, loaded once per session and then
/// answered from memory. Three layers are enforced: which countries, which pages, and which
/// milestone fields.
/// </summary>
public sealed class UserPermissions
{
    private readonly Dictionary<string, PagePermission> _pages;

    public UserPermissions(
        Guid userId,
        UserType userType,
        int? partnerId,
        string? supplierCompanyCode,
        IReadOnlyCollection<int> countryIds,
        IReadOnlyDictionary<string, PagePermission> pages)
    {
        UserId = userId;
        UserType = userType;
        PartnerId = partnerId;
        SupplierCompanyCode = supplierCompanyCode;
        CountryIds = countryIds;
        _pages = new Dictionary<string, PagePermission>(pages, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Permissions for an unauthenticated visitor: nothing at all.</summary>
    public static UserPermissions None { get; } =
        new(Guid.Empty, UserType.Broker, null, null, [], new Dictionary<string, PagePermission>());

    public Guid UserId { get; }

    public UserType UserType { get; }

    public int? PartnerId { get; }

    /// <summary>
    /// The Code of this account's row in LTS_LogisticsCompanies or LTS_Brokers (matching
    /// UserType) - what actually limits a Broker/LogisticsCompany account to its own shipments.
    /// </summary>
    public string? SupplierCompanyCode { get; }

    /// <summary>Countries the user may enter, in the order they are offered after login.</summary>
    public IReadOnlyCollection<int> CountryIds { get; }

    public bool IsAdmin => UserType == UserType.Admin;

    /// <summary>True when the user's rows must be restricted to their own partner.</summary>
    public bool IsPartnerScoped => UserType is UserType.Broker or UserType.LogisticsCompany;

    public bool HasCountry(int countryId) => IsAdmin || CountryIds.Contains(countryId);

    public PagePermission ForPage(string pageKey, int? countryId)
    {
        if (IsAdmin)
        {
            return PagePermission.Full;
        }

        if (countryId is { } id && !HasCountry(id))
        {
            return PagePermission.None;
        }

        return _pages.GetValueOrDefault(Key(pageKey, countryId), PagePermission.None);
    }

    public bool CanView(string pageKey, int? countryId) => ForPage(pageKey, countryId).CanView;

    public bool CanEdit(string pageKey, int? countryId) => ForPage(pageKey, countryId).CanEdit;

    /// <summary>
    /// Whether a milestone field is shown on the Shipment Details entry form. A logistics
    /// company sees only its own dates and a broker only theirs; internal staff see all of them.
    /// Grids are unaffected — they show every date read-only, because tracking needs the context.
    /// </summary>
    public bool CanViewMilestone(MilestoneType type)
    {
        if (IsAdmin || UserType == UserType.LogisticsDepartment)
        {
            return true;
        }

        return MilestoneCatalog.OwnerForUserType(UserType) == MilestoneCatalog.Get(type).Owner;
    }

    /// <summary>
    /// Whether a milestone date may actually be written. Requires edit rights on the Shipment
    /// Details page for the country, a milestone that accepts manual entry at all, and ownership
    /// of that milestone. Checked server-side on save, never only in the markup.
    /// </summary>
    public bool CanEditMilestone(MilestoneType type, int countryId)
    {
        var definition = MilestoneCatalog.Get(type);

        // Store pre-acceptance and acceptance come from the in-house service and are never typed in.
        if (!definition.AllowsManualEntry)
        {
            return false;
        }

        if (!CanEdit(PageKeys.ShipmentDetails, countryId))
        {
            return false;
        }

        return UserType switch
        {
            UserType.Admin or UserType.LogisticsDepartment => true,
            _ => MilestoneCatalog.OwnerForUserType(UserType) == definition.Owner
        };
    }

    /// <summary>Milestones this user may edit in the given country, in lifecycle order.</summary>
    public IReadOnlyList<MilestoneDefinition> EditableMilestones(int countryId) =>
        [.. MilestoneCatalog.All.Where(d => CanEditMilestone(d.Type, countryId))];

    /// <summary>
    /// Dictionary key for one grant. Cross-country admin pages use "*" so they cannot collide
    /// with a country-scoped grant for the same page.
    /// </summary>
    public static string Key(string pageKey, int? countryId) => $"{countryId?.ToString() ?? "*"}:{pageKey}";
}
