using ClosedXML.Excel;
using LTS.Application.Security;
using LTS.Application.Tracking;
using LTS.Domain.Enums;
using LTS.Domain.Milestones;

namespace LTS.Application.Excel;

/// <summary>
/// Bulk date entry by spreadsheet. The template only ever contains the columns the person
/// downloading it is allowed to fill in, and the upload is validated and previewed before a
/// single value is written.
/// </summary>
public interface IDateImportService
{
    /// <summary>Builds an upload template containing only the milestones this user may enter.</summary>
    byte[] BuildTemplate(UserPermissions permissions, int countryId);

    /// <summary>Reads an uploaded workbook without writing anything.</summary>
    DateImportPreview Parse(Stream stream, string fileName, UserPermissions permissions, int countryId);

    /// <summary>Writes the valid rows of a previously parsed workbook.</summary>
    Task<MilestoneApplyResult> CommitAsync(
        DateImportPreview preview,
        UserPermissions permissions,
        CancellationToken cancellationToken = default);

    /// <summary>Builds a workbook of the rows that failed, with the reason next to each one.</summary>
    byte[] BuildErrorReport(DateImportPreview preview, IReadOnlyList<MilestoneError> applyErrors);
}

public sealed class DateImportService(IIntegrationMilestoneService milestones) : IDateImportService
{
    private const string ShipmentSheet = "Shipment Dates";
    private const string TransferSheet = "Transfer Dates";
    private const int HeaderRow = 1;
    private const int FirstDataRow = 2;

    public byte[] BuildTemplate(UserPermissions permissions, int countryId)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        using var workbook = new XLWorkbook();

        var shipmentMilestones = Editable(permissions, countryId, MilestoneScope.Shipment);
        var transferMilestones = Editable(permissions, countryId, MilestoneScope.Transfer);

        // A sheet with no editable columns would just invite people to fill in cells that get
        // rejected, so it is left out entirely.
        if (shipmentMilestones.Count > 0)
        {
            BuildSheet(workbook, ShipmentSheet, "Reference No", shipmentMilestones,
                "REF-2026-00001");
        }

        if (transferMilestones.Count > 0)
        {
            BuildSheet(workbook, TransferSheet, "Transfer No", transferMilestones,
                "REF-2026-00001_TR100");
        }

        if (!workbook.Worksheets.Any())
        {
            workbook.AddWorksheet("No editable dates")
                .Cell(1, 1).Value = "Your account cannot enter any dates in this country.";
        }

        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);

        return buffer.ToArray();
    }

    public DateImportPreview Parse(Stream stream, string fileName, UserPermissions permissions, int countryId)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(permissions);

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception exception)
        {
            return DateImportPreview.Failed(fileName,
                $"The file could not be opened as an Excel workbook: {exception.Message}");
        }

        using (workbook)
        {
            var rows = new List<DateImportRow>();
            var fileErrors = new List<string>();

            ReadSheet(workbook, ShipmentSheet, MilestoneScope.Shipment, permissions, countryId, rows, fileErrors);
            ReadSheet(workbook, TransferSheet, MilestoneScope.Transfer, permissions, countryId, rows, fileErrors);

            if (rows.Count == 0 && fileErrors.Count == 0)
            {
                fileErrors.Add(
                    $"No date values were found. Expected a '{ShipmentSheet}' or '{TransferSheet}' sheet " +
                    "with a key column and at least one date column. Download the template to see the layout.");
            }

            return new DateImportPreview(fileName, rows, fileErrors);
        }
    }

    public Task<MilestoneApplyResult> CommitAsync(
        DateImportPreview preview,
        UserPermissions permissions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var changes = preview.ValidRows
            .Select(row => new MilestoneChange(row.Reference, row.Type, row.Date));

        // Permissions are enforced again here even though the template was already filtered:
        // the file could have been hand-edited between download and upload.
        var options = new MilestoneApplyOptions(
            MilestoneSource.ExcelUpload,
            Note: $"Uploaded from {preview.FileName}");

        return milestones.ApplyAsync(changes, options, permissions, cancellationToken);
    }

    public byte[] BuildErrorReport(DateImportPreview preview, IReadOnlyList<MilestoneError> applyErrors)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(applyErrors);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Errors");

        sheet.Cell(1, 1).Value = "Sheet";
        sheet.Cell(1, 2).Value = "Row";
        sheet.Cell(1, 3).Value = "Reference";
        sheet.Cell(1, 4).Value = "Date Field";
        sheet.Cell(1, 5).Value = "Value";
        sheet.Cell(1, 6).Value = "Problem";
        sheet.Row(1).Style.Font.Bold = true;

        var row = 2;

        foreach (var invalid in preview.InvalidRows)
        {
            sheet.Cell(row, 1).Value = invalid.Sheet;
            sheet.Cell(row, 2).Value = invalid.RowNumber;
            sheet.Cell(row, 3).Value = invalid.Reference;
            sheet.Cell(row, 4).Value = MilestoneCatalog.DisplayName(invalid.Type);
            sheet.Cell(row, 5).Value = invalid.RawValue ?? string.Empty;
            sheet.Cell(row, 6).Value = invalid.Error ?? string.Empty;
            row++;
        }

        // Rows that parsed cleanly but were rejected on save — unknown reference, wrong owner,
        // impossible chronology — matter just as much to whoever has to fix the file.
        foreach (var error in applyErrors)
        {
            sheet.Cell(row, 3).Value = error.Reference;
            sheet.Cell(row, 4).Value = error.Type is { } type ? MilestoneCatalog.DisplayName(type) : string.Empty;
            sheet.Cell(row, 6).Value = error.Message;
            row++;
        }

        sheet.Columns().AdjustToContents();

        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);

        return buffer.ToArray();
    }

    private static IReadOnlyList<MilestoneDefinition> Editable(
        UserPermissions permissions, int countryId, MilestoneScope scope) =>
        [.. MilestoneCatalog.ForScope(scope).Where(m => permissions.CanEditMilestone(m.Type, countryId))];

    private static void BuildSheet(
        XLWorkbook workbook,
        string name,
        string keyColumn,
        IReadOnlyList<MilestoneDefinition> milestones,
        string exampleKey)
    {
        var sheet = workbook.AddWorksheet(name);

        sheet.Cell(HeaderRow, 1).Value = keyColumn;
        for (var i = 0; i < milestones.Count; i++)
        {
            sheet.Cell(HeaderRow, i + 2).Value = milestones[i].DisplayName;
        }

        sheet.Row(HeaderRow).Style.Font.Bold = true;
        sheet.Row(HeaderRow).Style.Fill.BackgroundColor = XLColor.LightGray;
        sheet.SheetView.FreezeRows(HeaderRow);

        // A greyed-out example row shows the expected key format without being importable.
        sheet.Cell(FirstDataRow, 1).Value = exampleKey;
        sheet.Cell(FirstDataRow, 2).Value = DateTime.UtcNow.Date;
        sheet.Row(FirstDataRow).Style.Font.Italic = true;
        sheet.Row(FirstDataRow).Style.Font.FontColor = XLColor.Gray;
        sheet.Cell(FirstDataRow, milestones.Count + 2).Value = "← example row, delete before uploading";

        sheet.Range(FirstDataRow + 1, 2, 500, milestones.Count + 1).Style.DateFormat.Format = "yyyy-mm-dd";
        sheet.Columns().AdjustToContents();
    }

    private static void ReadSheet(
        XLWorkbook workbook,
        string sheetName,
        MilestoneScope scope,
        UserPermissions permissions,
        int countryId,
        List<DateImportRow> rows,
        List<string> fileErrors)
    {
        if (!workbook.TryGetWorksheet(sheetName, out var sheet))
        {
            return;
        }

        // Header text is matched back to milestones, so a user may delete columns they do not
        // need and reorder the rest without breaking the import.
        var columns = new Dictionary<int, MilestoneDefinition>();

        foreach (var cell in sheet.Row(HeaderRow).CellsUsed().Skip(1))
        {
            var header = cell.GetString().Trim();
            var milestone = MilestoneCatalog.ForScope(scope)
                .FirstOrDefault(m => string.Equals(m.DisplayName, header, StringComparison.OrdinalIgnoreCase));

            if (milestone is not null)
            {
                columns[cell.Address.ColumnNumber] = milestone;
            }
        }

        if (columns.Count == 0)
        {
            fileErrors.Add($"Sheet '{sheetName}' has no recognised date columns.");
            return;
        }

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? HeaderRow;

        for (var rowNumber = FirstDataRow; rowNumber <= lastRow; rowNumber++)
        {
            var reference = sheet.Cell(rowNumber, 1).GetString().Trim();
            if (reference.Length == 0)
            {
                continue;
            }

            foreach (var (columnNumber, milestone) in columns)
            {
                var cell = sheet.Cell(rowNumber, columnNumber);
                if (cell.IsEmpty())
                {
                    continue;
                }

                var raw = ExcelDates.RawValue(cell);

                if (!permissions.CanEditMilestone(milestone.Type, countryId))
                {
                    rows.Add(new DateImportRow(sheetName, rowNumber, reference, milestone.Type, null, raw,
                        $"Your account is not allowed to enter '{milestone.DisplayName}'."));
                    continue;
                }

                if (!ExcelDates.TryRead(cell, out var date, out var error))
                {
                    rows.Add(new DateImportRow(sheetName, rowNumber, reference, milestone.Type, null, raw, error));
                    continue;
                }

                if (date is null)
                {
                    continue;
                }

                rows.Add(new DateImportRow(sheetName, rowNumber, reference, milestone.Type, date, raw, null));
            }
        }
    }
}
