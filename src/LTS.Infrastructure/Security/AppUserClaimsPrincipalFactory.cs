using System.Security.Claims;
using LTS.Domain.Entities;
using LTS.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LTS.Infrastructure.Security;

/// <summary>
/// Adds the LTS-specific claims to the sign-in cookie so user type and partner are available
/// without a database round trip on every request.
/// </summary>
public sealed class AppUserClaimsPrincipalFactory(
    UserManager<AppUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, IdentityRole<Guid>>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        identity.AddClaim(new Claim(LtsClaimTypes.UserType, user.UserType.ToString()));
        identity.AddClaim(new Claim(LtsClaimTypes.FullName, user.FullName));

        if (user.PartnerId is { } partnerId)
        {
            identity.AddClaim(new Claim(LtsClaimTypes.PartnerId, partnerId.ToString()));
        }

        if (user.MustChangePassword)
        {
            identity.AddClaim(new Claim(LtsClaimTypes.MustChangePassword, "true"));
        }

        return identity;
    }
}
