namespace LTS.Application.Security;

/// <summary>
/// Loads what an account is allowed to do. Cached per user, so the permission tables are read
/// once rather than on every navigation, and invalidated the moment an admin changes a grant.
/// </summary>
public interface IPermissionService
{
    Task<UserPermissions> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Drops a user's cached permissions after their access is changed.</summary>
    void Invalidate(Guid userId);
}
