using ClosedXML.Excel;
using LTS.Application.Abstractions;
using LTS.Application.Excel;
using LTS.Application.Kpi;
using LTS.Application.Tracking;
using LTS.Domain.Entities;
using LTS.Domain.Enums;
using LTS.Domain.Kpi;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Kpi;

/// <summary>
/// Reads and writes KPI targets, including the spreadsheet round trip the logistics department
/// works in: export the current targets, edit them, upload them back.
/// </summary>
public sealed class KpiAdminService(LtsDbContext db, IKpiTargetProvider targets, IClock clock) : IKpiAdminService
{
    private const string SheetName = "KPI Targets";
    private const int HeaderRow = 1;

    public async Task<IReadOnlyList<KpiTargetRow>> GetTargetsAsync(
        int? arrivalCountryId = null, CancellationToken cancellationToken = default)
    {
        var query = db.KpiTargets
            .AsNoTracking()
            .Include(t => t.ExportType)
            .Include(t => t.ArrivalCountry)
            .AsQueryable();

        // A country's page shows its own targets plus the global fallbacks that also apply to it.
        if (arrivalCountryId is { } countryId)
        {
            query = query.Where(t => t.ArrivalCountryId == null || t.ArrivalCountryId == countryId);
        }

        var rows = await query.ToListAsync(cancellationToken);

        return
        [
            .. rows
                .OrderBy(t => t.Step)
                .ThenByDescending(t => t.Specificity)
                .Select(Map)
        ];
    }

    public async Task<int> SaveAsync(KpiTargetInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.TargetDays < 0)
        {
            throw new ArgumentException("A KPI target cannot be negative.", nameof(input));
        }

        var target = input.Id is { } id
            ? await db.KpiTargets.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                ?? throw new InvalidOperationException($"KPI target {id} does not exist.")
            : new KpiTarget { Step = input.Step, TargetDays = input.TargetDays };

        if (input.Id is null)
        {
            db.KpiTargets.Add(target);
        }

        Apply(target, input);
        await db.SaveChangesAsync(cancellationToken);

        // Scoring reads targets from a cache, so an edit has to invalidate it or the grids keep
        // showing performance based on the old numbers.
        targets.Invalidate();

        return target.Id;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var target = await db.KpiTargets.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (target is null)
        {
            return;
        }

        db.KpiTargets.Remove(target);
        await db.SaveChangesAsync(cancellationToken);
        targets.Invalidate();
    }

    public async Task<byte[]> ExportAsync(int? arrivalCountryId = null, CancellationToken cancellationToken = default)
    {
        var rows = await GetTargetsAsync(arrivalCountryId, cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(SheetName);

        var headers = new[]
        {
            "Step", "Export Type Code", "Loading Country", "Arrival Country",
            "Target Days", "Effective From", "Effective To", "Active"
        };

        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(HeaderRow, i + 1).Value = headers[i];
        }

        sheet.Row(HeaderRow).Style.Font.Bold = true;
        sheet.Row(HeaderRow).Style.Fill.BackgroundColor = XLColor.LightGray;
        sheet.SheetView.FreezeRows(HeaderRow);

        var exportTypeCodes = await db.LookupValues
            .AsNoTracking()
            .Where(l => l.Kind == LookupKind.ExportType)
            .ToDictionaryAsync(l => l.Id, l => l.Code, cancellationToken);

        var countryCodes = await db.Countries
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Code, cancellationToken);

        var row = HeaderRow + 1;

        // Every step is written out even when it has no target, so gaps are visible in the
        // sheet rather than being something you have to know to look for.
        var existing = rows.ToLookup(r => r.Step);

        foreach (var step in KpiStepCatalog.All)
        {
            var matches = existing[step.Step].ToList();

            if (matches.Count == 0)
            {
                sheet.Cell(row, 1).Value = step.DisplayName;
                row++;
                continue;
            }

            foreach (var target in matches)
            {
                sheet.Cell(row, 1).Value = step.DisplayName;
                sheet.Cell(row, 2).Value = target.ExportTypeId is { } id ? exportTypeCodes.GetValueOrDefault(id) : string.Empty;
                sheet.Cell(row, 3).Value = target.LoadingCountryCode ?? string.Empty;
                sheet.Cell(row, 4).Value = target.ArrivalCountryId is { } cid ? countryCodes.GetValueOrDefault(cid) : string.Empty;
                sheet.Cell(row, 5).Value = target.TargetDays;
                sheet.Cell(row, 6).Value = target.EffectiveFrom.ToDateTime(TimeOnly.MinValue);
                sheet.Cell(row, 6).Style.DateFormat.Format = "yyyy-mm-dd";

                if (target.EffectiveTo is { } to)
                {
                    sheet.Cell(row, 7).Value = to.ToDateTime(TimeOnly.MinValue);
                    sheet.Cell(row, 7).Style.DateFormat.Format = "yyyy-mm-dd";
                }

                sheet.Cell(row, 8).Value = target.IsActive ? "Yes" : "No";
                row++;
            }
        }

        sheet.Cell(row + 1, 1).Value =
            "Leave Export Type, Loading Country or Arrival Country blank to mean \"any\". " +
            "The most specific matching row wins.";
        sheet.Cell(row + 1, 1).Style.Font.Italic = true;

        sheet.Columns().AdjustToContents();

        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);

        return buffer.ToArray();
    }

    public async Task<KpiImportPreview> ParseAsync(
        Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception exception)
        {
            return KpiImportPreview.Failed(fileName, $"The file could not be opened: {exception.Message}");
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault(w => w.Name == SheetName)
                        ?? workbook.Worksheets.FirstOrDefault();

            if (sheet is null)
            {
                return KpiImportPreview.Failed(fileName, "The workbook has no worksheets.");
            }

            var exportTypes = await db.LookupValues
                .AsNoTracking()
                .Where(l => l.Kind == LookupKind.ExportType)
                .ToDictionaryAsync(l => l.Code.ToUpperInvariant(), l => l.Id, cancellationToken);

            var countries = await db.Countries
                .AsNoTracking()
                .ToDictionaryAsync(c => c.Code.ToUpperInvariant(), c => c.Id, cancellationToken);

            var rows = new List<KpiImportRow>();
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? HeaderRow;

            for (var rowNumber = HeaderRow + 1; rowNumber <= lastRow; rowNumber++)
            {
                var stepText = sheet.Cell(rowNumber, 1).GetString().Trim();
                if (stepText.Length == 0)
                {
                    continue;
                }

                var daysCell = sheet.Cell(rowNumber, 5);

                // A step with no target is a legitimate line in the exported sheet, not an error.
                if (daysCell.IsEmpty())
                {
                    continue;
                }

                rows.Add(ParseRow(rowNumber, sheet, stepText, exportTypes, countries));
            }

            var fileErrors = rows.Count == 0
                ? new List<string> { "No KPI target rows were found in the workbook." }
                : [];

            return new KpiImportPreview(fileName, rows, fileErrors);
        }
    }

    public async Task<KpiImportResult> CommitAsync(
        KpiImportPreview preview, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var created = 0;
        var updated = 0;
        var errors = new List<string>();

        foreach (var row in preview.ValidRows)
        {
            var input = row.Target!;

            try
            {
                // Matched on the full key so re-importing an edited sheet updates rows in place
                // instead of stacking duplicates that shadow each other.
                var existing = await db.KpiTargets.FirstOrDefaultAsync(t =>
                        t.Step == input.Step &&
                        t.ExportTypeId == input.ExportTypeId &&
                        t.LoadingCountryCode == input.LoadingCountryCode &&
                        t.ArrivalCountryId == input.ArrivalCountryId &&
                        t.EffectiveFrom == input.EffectiveFrom,
                    cancellationToken);

                if (existing is null)
                {
                    existing = new KpiTarget { Step = input.Step, TargetDays = input.TargetDays };
                    db.KpiTargets.Add(existing);
                    created++;
                }
                else
                {
                    updated++;
                }

                Apply(existing, input);
            }
            catch (Exception exception)
            {
                errors.Add($"Row {row.RowNumber}: {exception.Message}");
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        targets.Invalidate();

        return new KpiImportResult(created, updated, errors);
    }

    private KpiImportRow ParseRow(
        int rowNumber,
        IXLWorksheet sheet,
        string stepText,
        IReadOnlyDictionary<string, int> exportTypes,
        IReadOnlyDictionary<string, int> countries)
    {
        var step = KpiStepCatalog.All.FirstOrDefault(s =>
            string.Equals(s.DisplayName, stepText, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.Step.ToString(), stepText, StringComparison.OrdinalIgnoreCase));

        if (step is null)
        {
            return new KpiImportRow(rowNumber, null, stepText, $"'{stepText}' is not a known KPI step.");
        }

        var exportTypeCode = sheet.Cell(rowNumber, 2).GetString().Trim();
        int? exportTypeId = null;

        if (exportTypeCode.Length > 0)
        {
            if (!exportTypes.TryGetValue(exportTypeCode.ToUpperInvariant(), out var id))
            {
                return new KpiImportRow(rowNumber, null, step.DisplayName,
                    $"Export type '{exportTypeCode}' does not exist.");
            }

            exportTypeId = id;
        }

        var loadingCountry = sheet.Cell(rowNumber, 3).GetString().Trim();
        var arrivalCountryCode = sheet.Cell(rowNumber, 4).GetString().Trim();
        int? arrivalCountryId = null;

        if (arrivalCountryCode.Length > 0)
        {
            if (!countries.TryGetValue(arrivalCountryCode.ToUpperInvariant(), out var id))
            {
                return new KpiImportRow(rowNumber, null, step.DisplayName,
                    $"Arrival country '{arrivalCountryCode}' does not exist in LTS.");
            }

            arrivalCountryId = id;
        }

        if (!sheet.Cell(rowNumber, 5).TryGetValue<double>(out var days) || days < 0 || days % 1 != 0)
        {
            return new KpiImportRow(rowNumber, null, step.DisplayName,
                $"'{sheet.Cell(rowNumber, 5).GetString()}' is not a whole number of days.");
        }

        if (!ExcelDates.TryRead(sheet.Cell(rowNumber, 6), out var from, out var fromError))
        {
            return new KpiImportRow(rowNumber, null, step.DisplayName, $"Effective From: {fromError}");
        }

        if (!ExcelDates.TryRead(sheet.Cell(rowNumber, 7), out var to, out var toError))
        {
            return new KpiImportRow(rowNumber, null, step.DisplayName, $"Effective To: {toError}");
        }

        if (from is not null && to is not null && to < from)
        {
            return new KpiImportRow(rowNumber, null, step.DisplayName,
                "Effective To is before Effective From.");
        }

        var activeText = sheet.Cell(rowNumber, 8).GetString().Trim();
        var isActive = activeText.Length == 0 ||
                       activeText.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                       activeText.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                       activeText == "1";

        var target = new KpiTargetInput
        {
            Step = step.Step,
            ExportTypeId = exportTypeId,
            LoadingCountryCode = loadingCountry.Length == 0 ? null : loadingCountry.ToUpperInvariant(),
            ArrivalCountryId = arrivalCountryId,
            TargetDays = (int)days,
            // A sheet without an effective date is taken as "in force from today".
            EffectiveFrom = from ?? clock.Today,
            EffectiveTo = to,
            IsActive = isActive
        };

        var description = $"{step.DisplayName} · {(int)days} day(s)" +
                          $"{(exportTypeCode.Length > 0 ? $" · {exportTypeCode}" : string.Empty)}" +
                          $"{(loadingCountry.Length > 0 ? $" · from {loadingCountry}" : string.Empty)}" +
                          $"{(arrivalCountryCode.Length > 0 ? $" · to {arrivalCountryCode}" : string.Empty)}";

        return new KpiImportRow(rowNumber, target, description, null);
    }

    private static void Apply(KpiTarget target, KpiTargetInput input)
    {
        target.Step = input.Step;
        target.ExportTypeId = input.ExportTypeId;
        target.LoadingCountryCode = input.LoadingCountryCode;
        target.ArrivalCountryId = input.ArrivalCountryId;
        target.TargetDays = input.TargetDays;
        target.EffectiveFrom = input.EffectiveFrom;
        target.EffectiveTo = input.EffectiveTo;
        target.IsActive = input.IsActive;
    }

    private static KpiTargetRow Map(KpiTarget target) => new()
    {
        Id = target.Id,
        Step = target.Step,
        StepName = KpiStepCatalog.DisplayName(target.Step),
        ExportTypeId = target.ExportTypeId,
        ExportType = target.ExportType?.Name,
        LoadingCountryCode = target.LoadingCountryCode,
        ArrivalCountryId = target.ArrivalCountryId,
        ArrivalCountry = target.ArrivalCountry?.Name,
        TargetDays = target.TargetDays,
        EffectiveFrom = target.EffectiveFrom,
        EffectiveTo = target.EffectiveTo,
        IsActive = target.IsActive,
        Specificity = target.Specificity
    };
}
