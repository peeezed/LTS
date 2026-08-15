using LTS.Application.Abstractions;
using LTS.Domain.Enums;

namespace LTS.Infrastructure.Security;

/// <summary>
/// The identity background work runs under: no account, no partner, and no permission checks,
/// because the integration poller acts as the system rather than on anyone's behalf.
/// Audit rows written under it are stamped with their integration source instead.
/// </summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;

    public Guid? UserId => null;

    public string? UserName => null;

    public string? FullName => null;

    public UserType? UserType => null;

    public int? PartnerId => null;

    public bool IsAdmin => false;
}
