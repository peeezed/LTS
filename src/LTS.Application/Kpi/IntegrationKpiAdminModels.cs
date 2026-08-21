using LTS.Domain.Enums;

namespace LTS.Application.Kpi;

/// <summary>An LTS_Integration KPI target as shown in the admin grid, always for one specific country.</summary>
public sealed record IntegrationKpiTargetRow
{
    public required int Id { get; init; }
    public required IntegrationKpiStep Step { get; init; }
    public required string StepName { get; init; }

    /// <summary>Null means the target applies to any value of this attribute.</summary>
    public string? ExportType { get; init; }
    public string? LoadingPoint { get; init; }
    public string? ArrivalCustoms { get; init; }
    public string? TransportType { get; init; }

    public required int TargetDays { get; init; }
    public required bool IsActive { get; init; }

    /// <summary>How many of the 4 optional keys this row pins down; the highest matching value wins at scoring time.</summary>
    public required int Specificity { get; init; }
}

/// <summary>Values submitted when an administrator adds or edits a target. Country is not part of this - see <see cref="IIntegrationKpiAdminService.SaveAsync"/>.</summary>
public sealed record IntegrationKpiTargetInput
{
    public int? Id { get; init; }
    public required IntegrationKpiStep Step { get; init; }
    public string? ExportType { get; init; }
    public string? LoadingPoint { get; init; }
    public string? ArrivalCustoms { get; init; }
    public string? TransportType { get; init; }
    public required int TargetDays { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// Administration of LTS_Integration's KPI targets - always scoped to one country at a time (every
/// country has its own values), matching the admin page's own per-country route. countryId here is
/// the app-wide offset id (the same one CountryPageBase.CountryId already exposes) - the
/// implementation converts to LTS_Integration's own raw id internally, the same convention
/// ReferenceDataService/PermissionService/UserAdminService already follow.
/// </summary>
public interface IIntegrationKpiAdminService
{
    Task<IReadOnlyList<IntegrationKpiTargetRow>> GetTargetsAsync(
        int countryId, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates a target, always assigning it to <paramref name="countryId"/> - there is no way to move a target to a different country.</summary>
    Task<int> SaveAsync(int countryId, IntegrationKpiTargetInput input, CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
