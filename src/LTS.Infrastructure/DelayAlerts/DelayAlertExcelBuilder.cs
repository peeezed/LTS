using ClosedXML.Excel;
using LTS.Application.DelayAlerts;

namespace LTS.Infrastructure.DelayAlerts;

/// <summary>
/// Builds the delay alert mails' Excel attachments, following the same ClosedXML pattern as
/// DateImportService.BuildSheet/KpiAdminService.ExportAsync (bold+shaded frozen header row,
/// date-formatted date cells, columns sized to content).
/// </summary>
internal static class DelayAlertExcelBuilder
{
    private const int HeaderRow = 1;

    public static byte[] BuildShipmentReport(IReadOnlyList<ShipmentDelayAlertRow> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Shipment Delay Alert");

        string[] headers =
        [
            "Invoice No", "Export No", "Export Type", "Loading Point", "Arrival Customs", "Transport Type",
            "Logistics Company", "Broker Company", "Current Status", "Delay Phase", "Delayed Days",
            "Delay Start Date", "Delay End Date"
        ];

        WriteHeader(sheet, headers);

        var row = HeaderRow + 1;
        foreach (var r in rows)
        {
            var col = 1;
            sheet.Cell(row, col++).Value = r.InvoiceNo;
            sheet.Cell(row, col++).Value = r.ReferenceNo;
            sheet.Cell(row, col++).Value = r.ExportType ?? "";
            sheet.Cell(row, col++).Value = r.LoadingPoint ?? "";
            sheet.Cell(row, col++).Value = r.ArrivalCustoms ?? "";
            sheet.Cell(row, col++).Value = r.TransportType ?? "";
            sheet.Cell(row, col++).Value = r.LogisticsCompany ?? "";
            sheet.Cell(row, col++).Value = r.BrokerCompany ?? "";
            sheet.Cell(row, col++).Value = r.CurrentStatus;
            sheet.Cell(row, col++).Value = r.DelayPhase;
            sheet.Cell(row, col++).Value = r.DelayedDays;
            sheet.Cell(row, col++).Value = r.DelayStartDate.ToDateTime(TimeOnly.MinValue);
            if (r.DelayEndDate is { } end)
            {
                sheet.Cell(row, col).Value = end.ToDateTime(TimeOnly.MinValue);
            }
            row++;
        }

        FormatDateColumns(sheet, [12, 13], rows.Count);
        sheet.Columns().AdjustToContents();

        return ToBytes(workbook);
    }

    public static byte[] BuildTransferReport(IReadOnlyList<TransferDelayAlertRow> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Transfer Delay Alert");

        string[] headers =
        [
            "Invoice No", "Export No", "Transfer No", "Receiving Store", "Export Type", "Loading Point",
            "Arrival Customs", "Transport Type", "Logistics Company", "Broker Company", "Current Status",
            "Delay Phase", "Delayed Days", "Delay Start Date", "Delay End Date"
        ];

        WriteHeader(sheet, headers);

        var row = HeaderRow + 1;
        foreach (var r in rows)
        {
            var col = 1;
            sheet.Cell(row, col++).Value = r.InvoiceNo;
            sheet.Cell(row, col++).Value = r.ReferenceNo;
            sheet.Cell(row, col++).Value = r.TransferNo;
            sheet.Cell(row, col++).Value = r.ReceivingStore ?? "";
            sheet.Cell(row, col++).Value = r.ExportType ?? "";
            sheet.Cell(row, col++).Value = r.LoadingPoint ?? "";
            sheet.Cell(row, col++).Value = r.ArrivalCustoms ?? "";
            sheet.Cell(row, col++).Value = r.TransportType ?? "";
            sheet.Cell(row, col++).Value = r.LogisticsCompany ?? "";
            sheet.Cell(row, col++).Value = r.BrokerCompany ?? "";
            sheet.Cell(row, col++).Value = r.CurrentStatus;
            sheet.Cell(row, col++).Value = r.DelayPhase;
            sheet.Cell(row, col++).Value = r.DelayedDays;
            sheet.Cell(row, col++).Value = r.DelayStartDate.ToDateTime(TimeOnly.MinValue);
            if (r.DelayEndDate is { } end)
            {
                sheet.Cell(row, col).Value = end.ToDateTime(TimeOnly.MinValue);
            }
            row++;
        }

        FormatDateColumns(sheet, [14, 15], rows.Count);
        sheet.Columns().AdjustToContents();

        return ToBytes(workbook);
    }

    private static void WriteHeader(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            sheet.Cell(HeaderRow, i + 1).Value = headers[i];
        }

        sheet.Row(HeaderRow).Style.Font.Bold = true;
        sheet.Row(HeaderRow).Style.Fill.BackgroundColor = XLColor.LightGray;
        sheet.SheetView.FreezeRows(HeaderRow);
    }

    private static void FormatDateColumns(IXLWorksheet sheet, IReadOnlyList<int> columns, int rowCount)
    {
        foreach (var column in columns)
        {
            sheet.Range(HeaderRow + 1, column, HeaderRow + Math.Max(rowCount, 1), column).Style.DateFormat.Format = "yyyy-mm-dd";
        }
    }

    private static byte[] ToBytes(XLWorkbook workbook)
    {
        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        return buffer.ToArray();
    }
}
