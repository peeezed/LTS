using LTS.Domain.Enums;

namespace LTS.Application.Security;

/// <summary>An account as listed on the users admin page.</summary>
public sealed record UserRow
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public required string FullName { get; init; }
    public required UserType UserType { get; init; }

    /// <summary>
    /// The account's company, for display - "Code - Description" resolved from
    /// LTS_LogisticsCompanies/LTS_Brokers when SupplierCompanyCode is set, otherwise the old
    /// database's Partner name for accounts not yet re-linked, otherwise null.
    /// </summary>
    public string? CompanyDisplay { get; init; }

    public string? PartnerName { get; init; }
    public int? PartnerId { get; init; }
    public bool IsActive { get; init; }
    public bool MustChangePassword { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public IReadOnlyList<string> Countries { get; init; } = [];
}

/// <summary>Values submitted when creating or editing an account.</summary>
public sealed record UserInput
{
    public Guid? Id { get; init; }
    public required string Email { get; init; }
    public required string FullName { get; init; }
    public required UserType UserType { get; init; }

    /// <summary>
    /// The Code of a row in LTS_LogisticsCompanies (LogisticsCompany accounts) or LTS_Brokers
    /// (Broker accounts). Required for broker and logistics-company accounts; ignored for
    /// internal staff.
    /// </summary>
    public string? SupplierCompanyCode { get; init; }

    public bool IsActive { get; init; } = true;

    /// <summary>Countries the account may enter.</summary>
    public IReadOnlyList<int> CountryIds { get; init; } = [];

    /// <summary>
    /// Page grants. Country-scoped pages appear once per granted country; cross-country admin
    /// pages appear once with a null country.
    /// </summary>
    public IReadOnlyList<PagePermissionInput> Permissions { get; init; } = [];
}

public sealed record PagePermissionInput(int? CountryId, string PageKey, bool CanView, bool CanEdit);

/// <summary>Outcome of an account operation, including the generated password when there is one.</summary>
public sealed record UserSaveResult(bool Succeeded, Guid? UserId, string? TemporaryPassword, IReadOnlyList<string> Errors)
{
    public static UserSaveResult Failed(params string[] errors) => new(false, null, null, errors);
}

/// <summary>
/// Account administration. There is no self-registration in LTS: brokers and logistics
/// companies are onboarded deliberately by an administrator, who also decides which countries
/// and pages they get.
/// </summary>
public interface IUserAdminService
{
    Task<IReadOnlyList<UserRow>> GetUsersAsync(CancellationToken cancellationToken = default);

    Task<UserInput?> GetUserAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an account with a generated one-time password, or updates an existing one.
    /// </summary>
    Task<UserSaveResult> SaveAsync(UserInput input, CancellationToken cancellationToken = default);

    /// <summary>Issues a new one-time password and forces a change at next sign-in.</summary>
    Task<UserSaveResult> ResetPasswordAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
}
