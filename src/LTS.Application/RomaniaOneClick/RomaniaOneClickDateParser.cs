using System.Globalization;

namespace LTS.Application.RomaniaOneClick;

/// <summary>
/// Parses KLG OneClick's date fields (ISO 8601 datetime strings, e.g. "2023-06-21T00:00:00.000000Z")
/// into the DateOnly values LTS milestones are stored as. Pure and side-effect free - the caller
/// decides what to do (e.g. log) when parsing fails.
/// </summary>
public static class RomaniaOneClickDateParser
{
    public static DateOnly? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? DateOnly.FromDateTime(parsed)
            : null;
    }
}
