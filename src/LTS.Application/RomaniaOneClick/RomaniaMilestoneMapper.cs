using LTS.Application.Tracking;
using LTS.Domain.Enums;

namespace LTS.Application.RomaniaOneClick;

/// <summary>
/// Maps one KLG OneClick lookup result onto the MilestoneChange list RomaniaShipmentFeedRunner
/// applies through IIntegrationMilestoneService.ApplyAsync. Pure and side-effect free, mirroring
/// ShipmentStandardizer's split from ShipmentFeedRunner.
/// </summary>
public static class RomaniaMilestoneMapper
{
    /// <summary>
    /// shipment_date is intentionally excluded - it maps to the shipment-scope Crossdock Arrival
    /// milestone, which this per-transfer feed does not write in this version.
    /// </summary>
    public static IReadOnlyList<MilestoneChange> BuildMilestoneChanges(string transferNo, RomaniaDomesticShipmentDto shipment)
    {
        var changes = new List<MilestoneChange>();

        if (shipment.LoadingActStartDate is { } crossdockDeparture)
        {
            changes.Add(new MilestoneChange(transferNo, MilestoneType.CrossdockDeparture, crossdockDeparture));
        }

        if (shipment.UnloadingStartDate is { } plannedStoreArrival)
        {
            changes.Add(new MilestoneChange(transferNo, MilestoneType.PlannedStoreArrival, plannedStoreArrival));
        }

        if (shipment.UnloadingActStartDate is { } storeArrival)
        {
            changes.Add(new MilestoneChange(transferNo, MilestoneType.StoreArrival, storeArrival));
        }

        return changes;
    }
}
