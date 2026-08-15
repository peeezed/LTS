using LTS.Application.Security;
using LTS.Domain.Enums;

namespace LTS.Application.Tracking;

/// <summary>One recorded change to a milestone date.</summary>
public sealed record AuditRow
{
    public required int Id { get; init; }
    public required string ReferenceNo { get; init; }
    public string? TransferNo { get; init; }
    public required MilestoneType MilestoneType { get; init; }
    public required string MilestoneName { get; init; }
    public DateOnly? OldValue { get; init; }
    public DateOnly? NewValue { get; init; }
    public required MilestoneSource Source { get; init; }
    public string? UserName { get; init; }
    public DateTime ChangedAt { get; init; }
    public string? Note { get; init; }
}

/// <summary>Filters for the audit log page.</summary>
public sealed record AuditFilter
{
    /// <summary>Matches reference number or transfer number.</summary>
    public string? Search { get; init; }

    public MilestoneSource? Source { get; init; }

    public MilestoneType? MilestoneType { get; init; }

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }
}

/// <summary>
/// Reads the milestone change history. This is what answers "who changed that date, and to
/// what" — including when an integration overwrote something a person had typed.
/// </summary>
public interface IAuditQueryService
{
    Task<PagedResult<AuditRow>> GetAuditAsync(
        int countryId,
        UserPermissions permissions,
        AuditFilter filter,
        GridRequest request,
        CancellationToken cancellationToken = default);
}
