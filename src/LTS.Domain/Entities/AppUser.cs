using LTS.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace LTS.Domain.Entities;

/// <summary>
/// An LTS account. Accounts are only ever created by an administrator — there is no
/// self-registration, because brokers and logistics companies are onboarded deliberately.
/// </summary>
public class AppUser : IdentityUser<Guid>
{
    public required string FullName { get; set; }

    public required UserType UserType { get; set; }

    /// <summary>
    /// The old database's Partner this account was linked to. Superseded by
    /// <see cref="SupplierCompanyCode"/> for shipment scoping - kept only because existing
    /// accounts still carry a value here; new and edited accounts no longer set it.
    /// </summary>
    public int? PartnerId { get; set; }
    public Partner? Partner { get; set; }

    /// <summary>
    /// The Code of a row in LTS_LogisticsCompanies (LogisticsCompany accounts) or LTS_Brokers
    /// (Broker accounts) - what actually limits an external account to its own shipments now.
    /// Required for broker and logistics-company accounts; null for internal staff.
    /// </summary>
    public string? SupplierCompanyCode { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Set when an admin issues or resets a password; cleared once the user chooses their own.</summary>
    public bool MustChangePassword { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public ICollection<UserCountryAccess> CountryAccess { get; set; } = [];
    public ICollection<UserPagePermission> PagePermissions { get; set; } = [];

    /// <summary>True when the account's rows must be filtered to its own partner.</summary>
    public bool IsExternal => UserType is UserType.Broker or UserType.LogisticsCompany;
}

/// <summary>Grants a user access to one country; drives the post-login country chooser.</summary>
public class UserCountryAccess
{
    public required Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public required int CountryId { get; set; }
    public Country? Country { get; set; }
}

/// <summary>
/// What a user may see and do on one page of one country. Country-scoped so the same person
/// can, for example, edit dates in Turkey but only view them in Germany.
/// </summary>
public class UserPagePermission
{
    public int Id { get; set; }

    public required Guid UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Null for pages that are not country-scoped, such as user administration.</summary>
    public int? CountryId { get; set; }
    public Country? Country { get; set; }

    /// <summary>A value from <see cref="Security.PageKeys"/>.</summary>
    public required string PageKey { get; set; }

    public bool CanView { get; set; }

    public bool CanEdit { get; set; }
}
