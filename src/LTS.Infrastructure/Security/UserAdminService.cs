using System.Security.Cryptography;
using LTS.Application.Abstractions;
using LTS.Application.Security;
using LTS.Domain.Entities;
using LTS.Domain.Enums;
using LTS.Domain.Security;
using LTS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Security;

public sealed class UserAdminService(
    LtsDbContext db,
    UserManager<AppUser> users,
    IPermissionService permissions,
    ICurrentUser currentUser,
    IClock clock) : IUserAdminService
{
    public async Task<IReadOnlyList<UserRow>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
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
                PartnerName = u.Partner!.Name,
                u.IsActive,
                u.MustChangePassword,
                u.LastLoginAt,
                Countries = db.UserCountryAccess
                    .Where(a => a.UserId == u.Id)
                    .Select(a => a.Country!.Code)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(u => new UserRow
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                FullName = u.FullName,
                UserType = u.UserType,
                PartnerId = u.PartnerId,
                PartnerName = u.PartnerName,
                IsActive = u.IsActive,
                MustChangePassword = u.MustChangePassword,
                LastLoginAt = u.LastLoginAt,
                Countries = u.Countries
            })
        ];
    }

    public async Task<UserInput?> GetUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var countries = await db.UserCountryAccess
            .AsNoTracking()
            .Where(a => a.UserId == id)
            .Select(a => a.CountryId)
            .ToListAsync(cancellationToken);

        var grants = await db.UserPagePermissions
            .AsNoTracking()
            .Where(p => p.UserId == id)
            .Select(p => new PagePermissionInput(p.CountryId, p.PageKey, p.CanView, p.CanEdit))
            .ToListAsync(cancellationToken);

        return new UserInput
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            UserType = user.UserType,
            PartnerId = user.PartnerId,
            IsActive = user.IsActive,
            CountryIds = countries,
            Permissions = grants
        };
    }

    public async Task<UserSaveResult> SaveAsync(UserInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // An external account without a partner would see every shipment in its countries,
        // which is the opposite of what the partner scope is for.
        if (input.UserType is UserType.Broker or UserType.LogisticsCompany && input.PartnerId is null)
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
            PartnerId = input.UserType is UserType.Broker or UserType.LogisticsCompany ? input.PartnerId : null,
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

        await ReplaceAccessAsync(user.Id, input, cancellationToken);
        permissions.Invalidate(user.Id);

        return new UserSaveResult(true, user.Id, password, []);
    }

    private async Task<UserSaveResult> UpdateAsync(Guid id, UserInput input, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return UserSaveResult.Failed("That account no longer exists.");
        }

        user.FullName = input.FullName.Trim();
        user.UserType = input.UserType;
        user.PartnerId = input.UserType is UserType.Broker or UserType.LogisticsCompany ? input.PartnerId : null;
        user.IsActive = input.IsActive;

        if (!string.Equals(user.Email, input.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            user.Email = input.Email.Trim();
            user.UserName = input.Email.Trim();
            user.NormalizedEmail = users.NormalizeEmail(user.Email);
            user.NormalizedUserName = users.NormalizeName(user.UserName);
        }

        await ReplaceAccessAsync(id, input, cancellationToken);
        permissions.Invalidate(id);

        return new UserSaveResult(true, id, null, []);
    }

    /// <summary>
    /// Replaces country and page grants wholesale. The editor always submits the complete
    /// picture, so a diff would only add a way for a removed grant to survive.
    /// </summary>
    private async Task ReplaceAccessAsync(Guid userId, UserInput input, CancellationToken cancellationToken)
    {
        var existingCountries = await db.UserCountryAccess.Where(a => a.UserId == userId).ToListAsync(cancellationToken);
        db.UserCountryAccess.RemoveRange(existingCountries);

        var existingPermissions = await db.UserPagePermissions.Where(p => p.UserId == userId).ToListAsync(cancellationToken);
        db.UserPagePermissions.RemoveRange(existingPermissions);

        await db.SaveChangesAsync(cancellationToken);

        foreach (var countryId in input.CountryIds.Distinct())
        {
            db.UserCountryAccess.Add(new UserCountryAccess { UserId = userId, CountryId = countryId });
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
                CountryId = grant.CountryId,
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
