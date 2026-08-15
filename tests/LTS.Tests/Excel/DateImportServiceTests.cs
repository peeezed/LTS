using ClosedXML.Excel;
using FluentAssertions;
using LTS.Application.Excel;
using LTS.Application.Security;
using LTS.Application.Tracking;
using LTS.Domain.Enums;
using LTS.Domain.Security;

namespace LTS.Tests.Excel;

public class DateImportServiceTests
{
    private const int Turkey = 1;

    private readonly RecordingMilestoneService _milestones = new();
    private readonly DateImportService _service;

    public DateImportServiceTests() => _service = new DateImportService(_milestones);

    private static UserPermissions Permissions(UserType userType, bool canEdit = true)
    {
        var pages = new Dictionary<string, PagePermission>
        {
            [UserPermissions.Key(PageKeys.ShipmentDetails, Turkey)] = new(true, canEdit),
            [UserPermissions.Key(PageKeys.DateUpload, Turkey)] = new(true, canEdit)
        };

        return new UserPermissions(Guid.NewGuid(), userType, partnerId: 7, [Turkey], pages);
    }

    private static MemoryStream Workbook(Action<XLWorkbook> build)
    {
        using var workbook = new XLWorkbook();
        build(workbook);

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        return stream;
    }

    private static MemoryStream ShipmentSheet(params (string Reference, string Header, object Value)[] cells) =>
        Workbook(workbook =>
        {
            var sheet = workbook.AddWorksheet("Shipment Dates");
            var headers = cells.Select(c => c.Header).Distinct().ToList();

            sheet.Cell(1, 1).Value = "Reference No";
            for (var i = 0; i < headers.Count; i++)
            {
                sheet.Cell(1, i + 2).Value = headers[i];
            }

            var references = cells.Select(c => c.Reference).Distinct().ToList();

            foreach (var cell in cells)
            {
                var row = references.IndexOf(cell.Reference) + 2;
                sheet.Cell(row, 1).Value = cell.Reference;
                sheet.Cell(row, headers.IndexOf(cell.Header) + 2).Value = XLCellValue.FromObject(cell.Value);
            }
        });

    [Fact]
    public void The_template_only_contains_the_dates_the_downloader_may_enter()
    {
        var bytes = _service.BuildTemplate(Permissions(UserType.Broker), Turkey);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Shipment Dates");
        var headers = sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();

        headers.Should().Contain(["Reference No", "Customs Start", "Customs End"]);
        headers.Should().NotContain("Loading");
        headers.Should().NotContain("Departure");

        // A broker owns no transfer-level dates, so that sheet is not offered at all.
        workbook.Worksheets.Any(w => w.Name == "Transfer Dates").Should().BeFalse();
    }

    [Fact]
    public void A_logistics_company_gets_its_own_columns_instead()
    {
        var bytes = _service.BuildTemplate(Permissions(UserType.LogisticsCompany), Turkey);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var headers = workbook.Worksheet("Shipment Dates").Row(1).CellsUsed().Select(c => c.GetString()).ToList();

        headers.Should().Contain("Loading");
        headers.Should().NotContain("Customs Start");
    }

    [Fact]
    public void Valid_rows_are_parsed_with_their_dates()
    {
        using var stream = ShipmentSheet(("REF-1", "Customs Start", new DateTime(2026, 3, 10)));

        var preview = _service.Parse(stream, "dates.xlsx", Permissions(UserType.Broker), Turkey);

        preview.ValidCount.Should().Be(1);
        preview.InvalidCount.Should().Be(0);

        var row = preview.ValidRows.Single();
        row.Reference.Should().Be("REF-1");
        row.Type.Should().Be(MilestoneType.CustomsStart);
        row.Date.Should().Be(new DateOnly(2026, 3, 10));
    }

    [Fact]
    public void Text_dates_are_accepted_in_the_formats_people_actually_send()
    {
        using var stream = ShipmentSheet(("REF-1", "Customs Start", "10.03.2026"));

        var preview = _service.Parse(stream, "dates.xlsx", Permissions(UserType.Broker), Turkey);

        preview.ValidRows.Single().Date.Should().Be(new DateOnly(2026, 3, 10));
    }

    [Fact]
    public void An_unreadable_value_is_rejected_with_the_cell_contents_echoed_back()
    {
        using var stream = ShipmentSheet(("REF-1", "Customs Start", "not a date"));

        var preview = _service.Parse(stream, "dates.xlsx", Permissions(UserType.Broker), Turkey);

        preview.ValidCount.Should().Be(0);
        var row = preview.InvalidRows.Single();
        row.Error.Should().Contain("not a date");
        row.RawValue.Should().Be("not a date");
    }

    [Fact]
    public void A_hand_added_column_the_uploader_does_not_own_is_rejected_not_silently_dropped()
    {
        // The template would never contain "Loading" for a broker, so this file was edited.
        using var stream = ShipmentSheet(("REF-1", "Loading", new DateTime(2026, 3, 1)));

        var preview = _service.Parse(stream, "tampered.xlsx", Permissions(UserType.Broker), Turkey);

        preview.ValidCount.Should().Be(0);
        preview.InvalidRows.Single().Error.Should().Contain("not allowed");
    }

    [Fact]
    public void Good_and_bad_rows_in_one_file_are_reported_separately()
    {
        using var stream = ShipmentSheet(
            ("REF-1", "Customs Start", new DateTime(2026, 3, 10)),
            ("REF-2", "Customs Start", "rubbish"),
            ("REF-3", "Customs Start", new DateTime(2026, 3, 12)));

        var preview = _service.Parse(stream, "mixed.xlsx", Permissions(UserType.Broker), Turkey);

        preview.ValidCount.Should().Be(2);
        preview.InvalidCount.Should().Be(1);
        preview.CanImport.Should().BeTrue("the good rows should still be importable");
    }

    [Fact]
    public void Empty_cells_are_skipped_rather_than_treated_as_clearing_the_date()
    {
        using var stream = Workbook(workbook =>
        {
            var sheet = workbook.AddWorksheet("Shipment Dates");
            sheet.Cell(1, 1).Value = "Reference No";
            sheet.Cell(1, 2).Value = "Customs Start";
            sheet.Cell(2, 1).Value = "REF-1";
        });

        var preview = _service.Parse(stream, "empty.xlsx", Permissions(UserType.Broker), Turkey);

        preview.Rows.Should().BeEmpty();
        preview.FileErrors.Should().ContainSingle();
    }

    [Fact]
    public void A_file_that_is_not_a_workbook_fails_cleanly()
    {
        using var stream = new MemoryStream("this is not a spreadsheet"u8.ToArray());

        var preview = _service.Parse(stream, "notes.txt", Permissions(UserType.Broker), Turkey);

        preview.FileErrors.Should().ContainSingle();
        preview.CanImport.Should().BeFalse();
    }

    [Fact]
    public async Task Committing_sends_only_the_valid_rows_and_marks_them_as_an_upload()
    {
        using var stream = ShipmentSheet(
            ("REF-1", "Customs Start", new DateTime(2026, 3, 10)),
            ("REF-2", "Customs Start", "rubbish"));

        var permissions = Permissions(UserType.Broker);
        var preview = _service.Parse(stream, "mixed.xlsx", permissions, Turkey);

        await _service.CommitAsync(preview, permissions);

        _milestones.Changes.Should().ContainSingle()
            .Which.Reference.Should().Be("REF-1");
        _milestones.Options!.Source.Should().Be(MilestoneSource.ExcelUpload);
        _milestones.Options.Note.Should().Contain("mixed.xlsx");

        // Permissions travel with the commit: the file could have been edited after download.
        _milestones.Options.EnforcePermissions.Should().BeTrue();
    }

    [Fact]
    public void The_error_report_lists_both_parse_failures_and_save_rejections()
    {
        using var stream = ShipmentSheet(("REF-1", "Customs Start", "rubbish"));
        var preview = _service.Parse(stream, "mixed.xlsx", Permissions(UserType.Broker), Turkey);

        var applyErrors = new[]
        {
            new MilestoneError("REF-9", MilestoneType.CustomsStart, "No shipment found with reference 'REF-9'.")
        };

        var bytes = _service.BuildErrorReport(preview, applyErrors);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Errors");
        var text = string.Join("|", sheet.CellsUsed().Select(c => c.GetString()));

        text.Should().Contain("REF-1");
        text.Should().Contain("REF-9");
        text.Should().Contain("No shipment found");
    }

    /// <summary>Captures what the import handed to the milestone service without touching a database.</summary>
    private sealed class RecordingMilestoneService : IMilestoneService
    {
        public List<MilestoneChange> Changes { get; } = [];
        public MilestoneApplyOptions? Options { get; private set; }

        public Task<MilestoneApplyResult> ApplyAsync(
            IEnumerable<MilestoneChange> changes,
            MilestoneApplyOptions options,
            UserPermissions permissions,
            CancellationToken cancellationToken = default)
        {
            Changes.AddRange(changes);
            Options = options;

            return Task.FromResult(new MilestoneApplyResult(Changes.Count, 0, []));
        }
    }
}
