using LTS.Domain.Entities;
using LTS.Domain.Enums;
using LTS.Domain.Milestones;

namespace LTS.Domain.Services;

/// <summary>
/// Derives the current lifecycle position from the milestone dates that exist. Dates are the
/// facts; status is only ever a projection of them, so the two can never drift apart.
/// </summary>
public static class TrackingStatusCalculator
{
    /// <summary>The furthest status a shipment has reached, and the date it reached it.</summary>
    public static (TrackingStatus Status, DateOnly? Date) ForShipment(Shipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        return Furthest(
            MilestoneCatalog.ShipmentMilestones,
            shipment.GetMilestoneDate,
            TrackingStatus.Created,
            null);
    }

    /// <summary>
    /// The furthest status a transfer has reached. Before it leaves the crossdock a transfer
    /// has no dates of its own, so it inherits its shipment's status — which is what the
    /// Transfers grid shows.
    /// </summary>
    public static (TrackingStatus Status, DateOnly? Date) ForTransfer(Transfer transfer, Shipment? shipment = null)
    {
        ArgumentNullException.ThrowIfNull(transfer);

        shipment ??= transfer.Shipment;
        var (inherited, inheritedDate) = shipment is null
            ? (TrackingStatus.Created, (DateOnly?)null)
            : ForShipment(shipment);

        return Furthest(
            MilestoneCatalog.TransferMilestones,
            transfer.GetMilestoneDate,
            inherited,
            inheritedDate);
    }

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
