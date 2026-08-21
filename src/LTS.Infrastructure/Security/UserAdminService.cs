using System.Security.Cryptography;
using LTS.Application.Abstractions;
using LTS.Application.Security;
using LTS.Domain.Entities;
using LTS.Domain.Enums;
using LTS.Domain.Security;
using LTS.Infrastructure.Persistence;
using LTS.Infrastructure.Reference;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Security;

/// <summary>
/// Accounts and their grants live in LtsIntegrationDbContext. A Broker/LogisticsCompany account's
/// company is SupplierCompanyCode, a Code into LTS_LogisticsCompanies/LTS_Brokers; the old
/// database's Partner (via PartnerId) used to be read for display, for accounts not yet re-linked,
/// but that database was dropped (see LtsIntegrationDbContext-era commit history) - PartnerId is
/// still stored on old accounts and still shown as a raw id, but PartnerName can no longer be
/// resolved from anywhere, so GetUsersAsync no longer tries.
/// Each public method creates its own short-lived LtsIntegrationDbContext via the factory rather
/// than sharing one across the request/circuit - it used to share the scoped instance with
/// Identity's own UserManager store, which occasionally raced with it.
/// </summary>
public sealed class UserAdminService(
    IDbContextFactory<LtsIntegrationDbContext> dbFactory,
    UserManager<AppUser> users,
    IPermissionService permissions,
    ICurrentUser currentUser,
    IClock clock) : IUserAdminService
{
    public async Task<IReadOnlyList<UserRow>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var countryCodes = await db.Countries
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.CountryCode, cancellationToken);

        var rows = await db.Users
            .AsNoTracking()
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.FullName,
                u.UserType,
                u.PartnerId,
                u.SupplierCompanyCode,
                u.IsActive,
                u.MustChangePassword,
                u.LastLoginAt,
                CountryIds = db.UserCountryAccess.Where(a => a.UserId == u.Id).Select(a => a.CountryId).ToList()
            })
            .ToListAsync(cancellationToken);

        // Resolved the same way the shipment attributes are: match the account's code against
        // the lookup table's Code column, show "Code - Description".
        var logisticsCodes = await db.LogisticsCompanyAttributes.AsNoTracking()
            .ToDictionaryAsync(a => a.Code, a => $"{a.Code} - {a.Description}", cancellationToken);
        var brokerCodes = await db.BrokerAttributes.AsNoTracking()
            .ToDictionaryAsync(a => a.Code, a => $"{a.Code} - {a.Description}", cancellationToken);

        return
        [
            .. rows.Select(u =>
            {
                var companyDisplay = u.SupplierCompanyCode is null
                    ? null
                    : (u.UserType == UserType.Broker ? brokerCodes : logisticsCodes)
                        .GetValueOrDefault(u.SupplierCompanyCode, u.SupplierCompanyCode);

                return new UserRow
                {
                    Id = u.Id,
                    Email = u.Email ?? string.Empty,
                    FullName = u.FullName,
                    UserType = u.UserType,
                    PartnerId = u.PartnerId,
                    PartnerName = null,
                    CompanyDisplay = companyDisplay,
                    IsActive = u.IsActive,
                    MustChangePassword = u.MustChangePassword,
                    LastLoginAt = u.LastLoginAt,
                    Countries = [.. u.CountryIds.Select(id => countryCodes.GetValueOrDefault(id, "?"))]
                };
            })
        ];
    }

    public async Task<UserInput?> GetUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return null;
        }

        // Stored as LTS_Integration's own raw ids; exposed to the editor in the same offset id
        // space as the country list it picks from (IReferenceDataService.GetIntegrationCountriesAsync).
        var countries = await db.UserCountryAccess
            .AsNoTracking()
            .Where(a => a.UserId == id)
            .Select(a => a.CountryId + IntegrationCountryId.Offset)
            .ToListAsync(cancellationToken);

        var grants = await db.UserPagePermissions
            .AsNoTracking()
            .Where(p => p.UserId == id)
            .Select(p => new PagePermissionInput(p.CountryId + IntegrationCountryId.Offset, p.PageKey, p.CanView, p.CanEdit))
            .ToListAsync(cancellationToken);

        return new UserInput
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            UserType = user.UserType,
            SupplierCompanyCode = user.SupplierCompanyCode,
            IsActive = user.IsActive,
            CountryIds = countries,
            Permissions = grants
        };
    }

    public async Task<UserSaveResult> SaveAsync(UserInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // An external account without a company would see every shipment in its countries,
        // which is the opposite of what the scope is for.
        if (input.UserType is UserType.Broker or UserType.LogisticsCompany && input.SupplierCompanyCode is null)
        {
            return UserSaveResult.Failed(
                $"A {input.UserType.ToDisplay()} account must be linked to a company.");
        }

        return input.Id is { } id
            ? await UpdateAsync(id, input, cancellationToken)
            : await CreateAsync(input, cancellationToken);
    }

    private async Task<UserSaveResult> CreateAsync(UserInput input, CancellationToken cancellationToken)
    {
        var password = GeneratePassword();

        var user = new AppUser
        {
            UserName = input.Email.Trim(),
            Email = input.Email.Trim(),
            EmailConfirmed = true,
            FullName = input.FullName.Trim(),
            UserType = input.UserType,
            SupplierCompanyCode = input.UserType is UserType.Broker or UserType.LogisticsCompany
                ? input.SupplierCompanyCode : null,
            IsActive = input.IsActive,
            MustChangePassword = true,
            CreatedAt = clock.UtcNow,
            CreatedBy = currentUser.UserName ?? "system"
        };

        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            return UserSaveResult.Failed([.. result.Errors.Select(e => e.Description)]);
        }

        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            await ReplaceAccessAsync(db, user.Id, input, cancellationToken);
        }

        permissions.Invalidate(user.Id);

        return new UserSaveResult(true, user.Id, password, []);
    }

    private async Task<UserSaveResult> UpdateAsync(Guid id, UserInput input, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return UserSaveResult.Failed("That account no longer exists.");
        }

        user.FullName = input.FullName.Trim();
        user.UserType = input.UserType;
        user.SupplierCompanyCode = input.UserType is UserType.Broker or UserType.LogisticsCompany
            ? input.SupplierCompanyCode : null;
        user.IsActive = input.IsActive;

        if (!string.Equals(user.Email, input.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            user.Email = input.Email.Trim();
            user.UserName = input.Email.Trim();
            user.NormalizedEmail = users.NormalizeEmail(user.Email);
            user.NormalizedUserName = users.NormalizeName(user.UserName);
        }

        await ReplaceAccessAsync(db, id, input, cancellationToken);
        permissions.Invalidate(id);

        return new UserSaveResult(true, id, null, []);
    }

    /// <summary>
    /// Replaces country and page grants wholesale. The editor always submits the complete
    /// picture, so a diff would only add a way for a removed grant to survive. Runs on the
    /// caller's own db instance, since in UpdateAsync it also needs to flush the tracked AppUser
    /// changes made just before this is called.
    /// </summary>
    private static async Task ReplaceAccessAsync(
        LtsIntegrationDbContext db, Guid userId, UserInput input, CancellationToken cancellationToken)
    {
        var existingCountries = await db.UserCountryAccess.Where(a => a.UserId == userId).ToListAsync(cancellationToken);
        db.UserCountryAccess.RemoveRange(existingCountries);

        var existingPermissions = await db.UserPagePermissions.Where(p => p.UserId == userId).ToListAsync(cancellationToken);
        db.UserPagePermissions.RemoveRange(existingPermissions);

        await db.SaveChangesAsync(cancellationToken);

        // input.CountryIds/grant.CountryId arrive in the offset id space (the editor picked from
        // GetIntegrationCountriesAsync); LTS_UserCountryAccess/LTS_UserPagePermissions store the
        // raw id their foreign key to LTS_Countries actually needs.
        foreach (var countryId in input.CountryIds.Distinct())
        {
            db.UserCountryAccess.Add(new UserCountryAccess
            {
                UserId = userId,
                CountryId = IntegrationCountryId.ToRawId(countryId)
            });
        }

        var seen = new HashSet<string>();

        foreach (var grant in input.Permissions)
        {
            if (!grant.CanView && !grant.CanEdit)
            {
                continue;
            }

            // A grant for a country the account cannot enter would never take effect, so it is
            // dropped rather than stored as a misleading row.
            if (grant.CountryId is { } countryId && !input.CountryIds.Contains(countryId))
            {
                continue;
            }

            if (!seen.Add(UserPermissions.Key(grant.PageKey, grant.CountryId)))
            {
                continue;
            }

            db.UserPagePermissions.Add(new UserPagePermission
            {
                UserId = userId,
                CountryId = grant.CountryId is { } gcid ? IntegrationCountryId.ToRawId(gcid) : null,
                PageKey = grant.PageKey,
                CanView = grant.CanView || grant.CanEdit,
                CanEdit = grant.CanEdit
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<UserSaveResult> ResetPasswordAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return UserSaveResult.Failed("That account no longer exists.");
        }

        var password = GeneratePassword();
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, password);

        if (!result.Succeeded)
        {
            return UserSaveResult.Failed([.. result.Errors.Select(e => e.Description)]);
        }

        user.MustChangePassword = true;
        await users.UpdateAsync(user);

        return new UserSaveResult(true, id, password, []);
    }

    public async Task SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return;
        }

        user.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);

        // Deactivation has to bite immediately, not when the cached permissions expire.
        permissions.Invalidate(id);
    }

    /// <summary>
    /// A random one-time password that satisfies the configured complexity rules. It is shown
    /// to the administrator once, and the account is forced to replace it at first sign-in.
    /// </summary>
    private static string GeneratePassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%*?";

        var characters = new List<char>
        {
            Pick(upper), Pick(upper),
            Pick(lower), Pick(lower), Pick(lower), Pick(lower),
            Pick(digits), Pick(digits),
            Pick(symbols), Pick(symbols)
        };

        // Shuffle so the character classes are not always in the same positions.
        for (var i = characters.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (characters[i], characters[j]) = (characters[j], characters[i]);
        }

        return new string([.. characters]);

        static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
    }
}
