using LTS.Domain.Enums;
using LTS.Domain.Milestones;

namespace LTS.Domain.Services;

/// <summary>
/// Derives the current lifecycle position from the milestone dates that exist. Dates are the
/// facts; status is only ever a projection of them, so the two can never drift apart.
/// </summary>
public static class TrackingStatusCalculator
{
    /// <summary>The furthest status reached by any shipment-scope milestone with a date.</summary>
    public static (TrackingStatus Status, DateOnly? Date) ForShipment(Func<MilestoneType, DateOnly?> dateOf) =>
        Furthest(MilestoneCatalog.ShipmentMilestones, dateOf, TrackingStatus.Created, null);

    /// <summary>
    /// The furthest status a transfer has reached. Before it leaves the crossdock a transfer has
    /// no dates of its own, so it inherits its shipment's status - see
    /// <see cref="ForShipment(Func{MilestoneType,DateOnly?})"/> for the shipment-side equivalent
    /// this is meant to be seeded from.
    /// </summary>
    public static (TrackingStatus Status, DateOnly? Date) ForTransfer(
        Func<MilestoneType, DateOnly?> dateOf, TrackingStatus shipmentStatus) =>
        Furthest(MilestoneCatalog.TransferMilestones, dateOf, shipmentStatus, null);

    private static (TrackingStatus, DateOnly?) Furthest(
        IEnumerable<MilestoneDefinition> milestones,
        Func<MilestoneType, DateOnly?> dateOf,
        TrackingStatus seedStatus,
        DateOnly? seedDate)
    {
        var status = seedStatus;
        var date = seedDate;

        foreach (var milestone in milestones)
        {
            // A planned date is a forecast, not an achievement, so it never advances the status.
            if (milestone.ReachedStatus is not { } reached || milestone.IsPlanned)
            {
                continue;
            }

            if (dateOf(milestone.Type) is not { } milestoneDate || reached <= status)
            {
                continue;
            }

            status = reached;
            date = milestoneDate;
        }

        return (status, date);
    }
}
