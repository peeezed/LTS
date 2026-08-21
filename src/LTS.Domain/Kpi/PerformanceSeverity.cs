using LTS.Domain.Enums;

namespace LTS.Domain.Kpi;

/// <summary>
/// Ranks performance outcomes so a shipment can be summarised by its worst step.
/// A leg that finished late is a problem; a leg that is still running past target is a
/// bigger one, because the clock has not stopped.
/// </summary>
public static class PerformanceSeverity
{
    public static int Rank(this PerformanceStatus status) => status switch
    {
        PerformanceStatus.NotStarted => 0,
        PerformanceStatus.NoTarget => 1,
        PerformanceStatus.OnTime => 2,
        PerformanceStatus.OnTrack => 3,
        PerformanceStatus.AtRisk => 4,
        PerformanceStatus.Late => 5,
        PerformanceStatus.Overdue => 6,

        // A shipment missing required attribute data can't be scored against any leg at all -
        // that's a more urgent problem than any single late/overdue leg, since it means KPI
        // visibility is blocked entirely until the data is fixed.
        PerformanceStatus.MissingAttributes => 7,
        _ => 0
    };

    public static PerformanceStatus Worst(IEnumerable<PerformanceStatus> statuses)
    {
        var worst = PerformanceStatus.NotStarted;
        foreach (var status in statuses)
        {
            if (status.Rank() > worst.Rank())
            {
                worst = status;
            }
        }

        return worst;
    }

    /// <summary>True when the step has missed, or is actively missing, its target.</summary>
    public static bool IsBreach(this PerformanceStatus status) =>
        status is PerformanceStatus.Late or PerformanceStatus.Overdue;
}
