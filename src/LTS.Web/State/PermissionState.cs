using LTS.Application.Abstractions;
using LTS.Application.Security;

namespace LTS.Web.State;

/// <summary>
/// The signed-in user's permissions for the lifetime of their circuit or request. Loaded once
/// and then answered from memory, so a page can ask "can this person edit?" as often as it likes
/// while rendering without touching the database.
/// </summary>
/// <remarks>
/// The in-flight <see cref="Task{TResult}"/> is cached rather than its result. Components on one
/// page initialise concurrently — the navigation menu and the page itself both need permissions
/// — and caching only the result would let both start their own query against the same scoped
/// <c>DbContext</c>, which EF Core refuses. Sharing the task means the first caller does the work
/// and the rest await it.
/// </remarks>
public sealed class PermissionState(IPermissionService permissions, ICurrentUser currentUser)
{
    private Task<UserPermissions>? _loading;

    public Task<UserPermissions> GetAsync(CancellationToken cancellationToken = default) =>
        _loading ??= LoadAsync(cancellationToken);

    /// <summary>Forces a reload after an admin changes the current user's own access.</summary>
    public void Reset() => _loading = null;

    private async Task<UserPermissions> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return currentUser.UserId is { } userId
                ? await permissions.GetAsync(userId, cancellationToken)
                : UserPermissions.None;
        }
        catch
        {
            // A failed load must not be cached, or the rest of the session would keep replaying
            // the same failure instead of retrying.
            _loading = null;
            throw;
        }
    }
}
