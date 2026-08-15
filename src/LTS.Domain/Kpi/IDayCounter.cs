namespace LTS.Domain.Kpi;

/// <summary>
/// Counts the days between two dates for KPI scoring. Abstracted so a country can later be
/// switched from calendar days to working days without touching the evaluator.
/// </summary>
public interface IDayCounter
{
    /// <summary>
    /// Days elapsed from <paramref name="from"/> to <paramref name="to"/>. Same day is 0.
    /// Negative when <paramref name="to"/> precedes <paramref name="from"/>.
    /// </summary>
    int DaysBetween(DateOnly from, DateOnly to);
}

/// <summary>Plain calendar days — weekends and holidays count. The current LTS default.</summary>
public sealed class CalendarDayCounter : IDayCounter
{
    public static readonly CalendarDayCounter Instance = new();

    public int DaysBetween(DateOnly from, DateOnly to) => to.DayNumber - from.DayNumber;
}
