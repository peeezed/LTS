using LTS.Domain.Enums;

namespace LTS.Application.Excel;

/// <summary>One date cell from an uploaded workbook, after parsing.</summary>
/// <param name="Sheet">Worksheet the value came from.</param>
/// <param name="RowNumber">Row in that sheet, so an error can be pointed at exactly.</param>
/// <param name="Reference">Reference number, invoice number or transfer number from the key column.</param>
/// <param name="Type">The milestone the column maps to.</param>
/// <param name="Date">The parsed date, when the cell could be read.</param>
/// <param name="RawValue">What the cell actually contained, echoed back in the error report.</param>
/// <param name="Error">Why the cell was rejected, or null when it is good to import.</param>
public sealed record DateImportRow(
    string Sheet,
    int RowNumber,
    string Reference,
    MilestoneType Type,
    DateOnly? Date,
    string? RawValue,
    string? Error)
{
    public bool IsValid => Error is null;
}

/// <summary>
/// The result of reading an uploaded workbook, shown for confirmation before anything is
/// written. Nothing is saved until the user has seen exactly what will change and what failed.
/// </summary>
public sealed record DateImportPreview(
    string FileName,
    IReadOnlyList<DateImportRow> Rows,
    IReadOnlyList<string> FileErrors)
{
    public static DateImportPreview Failed(string fileName, string error) => new(fileName, [], [error]);

    public IReadOnlyList<DateImportRow> ValidRows => [.. Rows.Where(r => r.IsValid)];

    public IReadOnlyList<DateImportRow> InvalidRows => [.. Rows.Where(r => !r.IsValid)];

    public int ValidCount => ValidRows.Count;

    public int InvalidCount => InvalidRows.Count;

    public bool CanImport => FileErrors.Count == 0 && ValidCount > 0;
}
