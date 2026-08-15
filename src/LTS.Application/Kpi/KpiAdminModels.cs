using LTS.Domain.Enums;

namespace LTS.Application.Kpi;

/// <summary>A KPI target as shown in the admin grid.</summary>
public sealed record KpiTargetRow
{
    public required int Id { get; init; }
    public required KpiStep Step { get; init; }
    public required string StepName { get; init; }

    public int? ExportTypeId { get; init; }

    /// <summary>Null means the target applies to any export type.</summary>
    public string? ExportType { get; init; }

    public string? LoadingCountryCode { get; init; }

    public int? ArrivalCountryId { get; init; }
    public string? ArrivalCountry { get; init; }

    public int TargetDays { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public bool IsActive { get; init; }

    /// <summary>How many keys the row pins down; the highest matching value wins at scoring time.</summary>
    public int Specificity { get; init; }
}

/// <summary>Values submitted when an administrator adds or edits a target.</summary>
public sealed record KpiTargetInput
{
    public int? Id { get; init; }
    public required KpiStep Step { get; init; }
    public int? ExportTypeId { get; init; }
    public string? LoadingCountryCode { get; init; }
    public int? ArrivalCountryId { get; init; }
    public required int TargetDays { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>One parsed row of a KPI import, with whatever is wrong with it.</summary>
public sealed record KpiImportRow(
    int RowNumber,
    KpiTargetInput? Target,
    string Description,
    string? Error)
{
    public bool IsValid => Error is null && Target is not null;
}

/// <summary>The result of reading a KPI workbook, shown before anything is saved.</summary>
public sealed record KpiImportPreview(
    string FileName,
    IReadOnlyList<KpiImportRow> Rows,
    IReadOnlyList<string> FileErrors)
{
    public static KpiImportPreview Failed(string fileName, string error) => new(fileName, [], [error]);

    public IReadOnlyList<KpiImportRow> ValidRows => [.. Rows.Where(r => r.IsValid)];
    public IReadOnlyList<KpiImportRow> InvalidRows => [.. Rows.Where(r => !r.IsValid)];
    public bool CanImport => FileErrors.Count == 0 && ValidRows.Count > 0;
}

/// <summary>What an import actually changed.</summary>
public sealed record KpiImportResult(int Created, int Updated, IReadOnlyList<string> Errors);

/// <summary>
/// Administration of the KPI targets the logistics department supplies. Targets arrive as a
/// spreadsheet in practice, so import is a first-class path rather than an afterthought.
/// </summary>
public interface IKpiAdminService
{
    Task<IReadOnlyList<KpiTargetRow>> GetTargetsAsync(
        int? arrivalCountryId = null, CancellationToken cancellationToken = default);

    Task<int> SaveAsync(KpiTargetInput input, CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Workbook containing every step and the current targets, ready to edit and re-upload.</summary>
    Task<byte[]> ExportAsync(int? arrivalCountryId = null, CancellationToken cancellationToken = default);

    Task<KpiImportPreview> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken = default);

    Task<KpiImportResult> CommitAsync(KpiImportPreview preview, CancellationToken cancellationToken = default);
}
