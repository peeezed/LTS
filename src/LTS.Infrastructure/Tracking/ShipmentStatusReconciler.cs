using LTS.Domain.Enums;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Tracking;

/// <summary>
/// Catches up LTS_Shipments.CurrentStatus and every one of its transfers' LTS_ShipmentTransfers.CurrentStatus
/// for shipments whose transfer dates changed outside the app - e.g. a future supplier/warehouse
/// integration writing straight into LTS_ShipmentTransferDates via SQL, bypassing
/// IntegrationMilestoneService entirely, so nothing ever triggers a recompute for that shipment.
/// The UI itself is unaffected by this gap (it always recomputes fresh at read time - see
/// IntegrationShipmentQueryService), but the stored columns only self-correct here, on a timer,
/// since nothing in the hand-written schema notifies the app when a row outside its own write
/// path changes.
/// </summary>
public sealed class ShipmentStatusReconciler(IDbContextFactory<LtsIntegrationDbContext> dbFactory)
{
    /// <summary>
    /// Only shipments whose own milestones have already reached AtCrossdock, or were previously
    /// caught up to InTransitToStore by an earlier pass, can have a transfer-driven status still
    /// to catch up on - anything earlier, or already ArrivedAtStore (the terminal status), is
    /// unaffected by transfer data and skipped to keep this cheap.
    /// </summary>
    private static readonly string[] CandidateStatuses =
    [
        TrackingStatus.AtCrossdock.ToDisplay(),
        TrackingStatus.InTransitToStore.ToDisplay()
    ];

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var shipments = await db.Shipments
            .Where(s => CandidateStatuses.Contains(s.CurrentStatus))
            .ToListAsync(cancellationToken);

        foreach (var shipment in shipments)
        {
            var snapshot = await ShipmentStatusAggregator.ComputeStatusesAsync(
                db, shipment.ReferenceNo, pendingShipmentDates: null, pendingTransferDates: null, cancellationToken);

            var display = snapshot.ShipmentStatus.ToDisplay();
            if (shipment.CurrentStatus != display)
            {
                shipment.CurrentStatus = display;
            }

            if (snapshot.TransferStatuses.Count == 0)
            {
                continue;
            }

            var transfers = await db.ShipmentTransfers
                .Where(t => t.ReferenceNo == shipment.ReferenceNo)
                .ToListAsync(cancellationToken);

            foreach (var transfer in transfers)
            {
                if (snapshot.TransferStatuses.TryGetValue(transfer.TransferNo, out var status))
                {
                    var transferDisplay = status.ToDisplay();
                    if (transfer.CurrentStatus != transferDisplay)
                    {
                        transfer.CurrentStatus = transferDisplay;
                    }
                }
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
