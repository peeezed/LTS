namespace LTS.Domain.Enums;

/// <summary>
/// Human-readable labels for the tracking enums. Kept in one place so a status reads the same
/// in a grid, a dashboard, an Excel export and a validation message.
/// </summary>
public static class StatusDisplay
{
    public static string ToDisplay(this TrackingStatus status) => status switch
    {
        TrackingStatus.Created => "Created",
        TrackingStatus.Loaded => "Loaded",
        TrackingStatus.ExportCustomsCleared => "Export Customs Cleared",
        TrackingStatus.Departed => "Departed",
        TrackingStatus.ArrivedInTargetCountry => "Arrived In Target Country",
        TrackingStatus.CustomsInProgress => "Customs In Progress",
        TrackingStatus.CustomsCleared => "Customs Cleared",
        TrackingStatus.AtCrossdock => "At Crossdock",
        TrackingStatus.InTransitToStore => "In Transit To Store",
        TrackingStatus.ArrivedAtStore => "Arrived At Store",
        TrackingStatus.PreAccepted => "Pre Accepted",
        TrackingStatus.Accepted => "Accepted",
        _ => status.ToString()
    };

    public static string ToDisplay(this PerformanceStatus status) => status switch
    {
        PerformanceStatus.NotStarted => "Not Started",
        PerformanceStatus.NoTarget => "No Target",
        PerformanceStatus.OnTrack => "On Track",
        PerformanceStatus.AtRisk => "At Risk",
        PerformanceStatus.Overdue => "Overdue",
        PerformanceStatus.OnTime => "On Time",
        PerformanceStatus.Late => "Late",
        PerformanceStatus.MissingAttributes => "Missing Attributes",
        _ => status.ToString()
    };

    public static string ToDisplay(this UserType userType) => userType switch
    {
        UserType.Admin => "Administrator",
        UserType.LogisticsDepartment => "Logistics Department",
        UserType.Broker => "Broker Company",
        UserType.LogisticsCompany => "Logistics Company",
        _ => userType.ToString()
    };

    public static string ToDisplay(this MilestoneSource source) => source switch
    {
        MilestoneSource.Manual => "Manual entry",
        MilestoneSource.ExcelUpload => "Excel upload",
        MilestoneSource.Integration => "Integration",
        MilestoneSource.InHouseService => "In-house service",
        MilestoneSource.Seed => "Seed data",
        _ => source.ToString()
    };
}
