using System.Security.Claims;
using LTS.Application.Abstractions;
using LTS.Domain.Enums;
using LTS.Domain.Security;

namespace LTS.Web.Security;

/// <summary>
/// The signed-in user, read from the cookie's claims. Registered over the infrastructure
/// default so services and the audit trail see a person rather than "system".
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? UserName => Principal?.FindFirstValue(ClaimTypes.Name);

    public string? FullName => Principal?.FindFirstValue(LtsClaimTypes.FullName);

    public UserType? UserType =>
        Enum.TryParse<UserType>(Principal?.FindFirstValue(LtsClaimTypes.UserType), out var type) ? type : null;

    public int? PartnerId =>
        int.TryParse(Principal?.FindFirstValue(LtsClaimTypes.PartnerId), out var id) ? id : null;

    public bool IsAdmin => UserType == Domain.Enums.UserType.Admin;
}
