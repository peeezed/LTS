using LTS.Domain.Enums;

namespace LTS.Application.Abstractions;

/// <summary>
/// The account behind the current request. Services depend on this rather than on
/// <c>HttpContext</c> so they stay testable and usable from the integration background worker,
/// where there is no user at all.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    string? UserName { get; }

    string? FullName { get; }

    UserType? UserType { get; }

    /// <summary>The company an external user belongs to; null for internal staff.</summary>
    int? PartnerId { get; }

    bool IsAdmin { get; }
}

/// <summary>
/// Supplies the current time. Injected rather than called statically so KPI scoring and
/// integration scheduling can be tested at a fixed point in time.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }

    DateOnly Today { get; }
}

/// <summary>System clock, used everywhere outside tests.</summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}
