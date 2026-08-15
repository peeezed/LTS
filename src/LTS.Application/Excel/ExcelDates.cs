using System.Globalization;
using ClosedXML.Excel;

namespace LTS.Application.Excel;

/// <summary>
/// Reads dates out of spreadsheet cells. Uploads come from many hands and many locales, so a
/// cell may hold a real date, a serial number, or text in whatever format the sender's Excel
/// produced — all of which have to be understood or clearly rejected.
/// </summary>
public static class ExcelDates
{
    /// <summary>Formats accepted when a cell holds text rather than a date value.</summary>
    private static readonly string[] TextFormats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd", "dd.MM.yyyy", "dd/MM/yyyy", "dd-MM-yyyy",
        "MM/dd/yyyy", "d.M.yyyy", "d/M/yyyy", "yyyyMMdd"
    ];

    /// <summary>
    /// Reads a cell as a date. Returns false with a message when the cell holds something that
    /// cannot be a date; an empty cell is "no value" rather than an error.
    /// </summary>
    public static bool TryRead(IXLCell cell, out DateOnly? date, out string? error)
    {
        date = null;
        error = null;

        if (cell.IsEmpty())
        {
            return true;
        }

        if (cell.DataType == XLDataType.DateTime && cell.TryGetValue<DateTime>(out var cellDate))
        {
            date = DateOnly.FromDateTime(cellDate);
            return true;
        }

        // A date column formatted as "General" arrives as the underlying serial number.
        if (cell.DataType == XLDataType.Number && cell.TryGetValue<double>(out var serial))
        {
            try
            {
                date = DateOnly.FromDateTime(DateTime.FromOADate(serial));
                return true;
            }
            catch (ArgumentException)
            {
                error = $"'{serial}' is not a valid date.";
                return false;
            }
        }

        var text = cell.GetString().Trim();
        if (text.Length == 0)
        {
            return true;
        }

        if (DateTime.TryParseExact(text, TextFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed) ||
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
        {
            date = DateOnly.FromDateTime(parsed);
            return true;
        }

        error = $"'{text}' is not a date LTS can read. Use yyyy-MM-dd.";
        return false;
    }

    /// <summary>The raw cell contents, for echoing back in an error report.</summary>
    public static string? RawValue(IXLCell cell) =>
        cell.IsEmpty() ? null : cell.GetFormattedString();
}
