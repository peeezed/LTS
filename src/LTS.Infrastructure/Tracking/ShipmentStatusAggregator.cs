using LTS.Application.Tracking;
using LTS.Domain.Enums;
using LTS.Domain.Services;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Tracking;

/// <summary>
/// Shared logic for deriving a transfer's status from its own dates (seeded by its shipment's),
/// and a shipment's status once its own milestones cap at AtCrossdock, from its transfers'.
/// Used by the read side (IntegrationShipmentQueryService, computed at query time for display)
/// and both write-side callers that persist the same values into LTS_Shipments.CurrentStatus and
/// LTS_ShipmentTransfers.CurrentStatus - IntegrationMilestoneService (immediately after an edit
/// made through the app) and ShipmentStatusReconciler (catching up rows changed some other way) -
/// living here rather than in any of them keeps the three from drifting apart.
/// </summary>
internal static class ShipmentStatusAggregator
{
    /// <summary>
    /// A transfer's displayed status: its shipment's status until the transfer has its own
    /// Crossdock Departure date, at which point it advances on its own milestones from there.
    /// </summary>
    public static TrackingStatus TransferStatus(
        TrackingStatus shipmentStatus,
        DateOnly? crossdockDeparture,
        DateOnly? plannedStoreArrival,
        DateOnly? storeArrival,
        DateOnly? storePreAcceptance,
        DateOnly? storeAcceptance)
    {
        var (status, _) = TrackingStatusCalculator.ForTransfer(type => type switch
        {
            MilestoneType.CrossdockDeparture => crossdockDeparture,
            MilestoneType.PlannedStoreArrival => plannedStoreArrival,
            MilestoneType.StoreArrival => storeArrival,
            MilestoneType.StorePreAcceptance => storePreAcceptance,
            MilestoneType.StoreAcceptance => storeAcceptance,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a transfer milestone.")
        }, shipmentStatus);

        return status;
    }

    /// <summary>
    /// A shipment's displayed status: its own milestone-derived status (which caps at
    /// AtCrossdock - LTS_Integration only tracks shipment-scope milestones that far) unless its
    /// transfers have moved further, in which case it follows them - InTransitToStore as soon as
    /// any one transfer has left the crossdock, ArrivedAtStore (the shipment's terminal status;
    /// there is no KPI target model to score anything past it) once every transfer has reached
    /// its store. A shipment before AtCrossdock, or with no transfers yet, is unaffected.
    /// </summary>
    public static TrackingStatus AggregateShipmentStatus(
        TrackingStatus milestoneStatus, IReadOnlyList<TransferStatusCount> transferBreakdown)
    {
        if (milestoneStatus != TrackingStatus.AtCrossdock || transferBreakdown.Count == 0)
        {
            return milestoneStatus;
        }

        if (transferBreakdown.All(b => b.Status >= TrackingStatus.ArrivedAtStore))
        {
            return TrackingStatus.ArrivedAtStore;
        }

        return transferBreakdown.Any(b => b.Status >= TrackingStatus.InTransitToStore)
            ? TrackingStatus.InTransitToStore
            : milestoneStatus;
    }

    /// <summary>
    /// A transfer-level milestone rolled up from its boxes: null until every box in the transfer
    /// has the date, then the latest of them.
    /// </summary>
    public static DateOnly? BoxMilestone(IReadOnlyList<LtsIntegrationBox> boxes, Func<LtsIntegrationBox, DateOnly?> selector) =>
        boxes.Count > 0 && boxes.All(b => selector(b) is not null) ? boxes.Max(selector) : null;

    public static DateOnly? GetDate(LtsIntegrationShipmentDate d, MilestoneType type) => type switch
    {
        MilestoneType.Loading => d.LoadingDate,
        MilestoneType.DepartureCustomsClearance => d.CustomsClearanceDate,
        MilestoneType.Departure => d.DepartureDate,
        MilestoneType.ArrivalToTargetCountry => d.ArrivalDate,
        MilestoneType.CustomsStart => d.ArrivalCustomsStartDate,
        MilestoneType.CustomsEnd => d.ArrivalCustomsEndDate,
        MilestoneType.CrossdockArrival => d.CrossdockArrivalDate,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a shipment milestone.")
    };

    /// <summary>
    /// A shipment's status from its own milestones alone (capped at AtCrossdock), ignoring
    /// whatever LTS_Shipments.CurrentStatus currently holds. This must never be read from that
    /// stored column: once a shipment's transfers have advanced it, the column holds the
    /// *aggregated* value (e.g. InTransitToStore), and feeding that back in as if it were the raw
    /// floor would make every one of its transfers - including ones with no dates of their own -
    /// inherit that already-aggregated value instead of the true AtCrossdock floor they should be
    /// seeded from. Always derive this from LTS_ShipmentDates directly instead.
    /// </summary>
    public static TrackingStatus MilestoneStatus(LtsIntegrationShipmentDate? shipmentDate) =>
        TrackingStatusCalculator.ForShipment(t => shipmentDate is null ? null : GetDate(shipmentDate, t)).Status;

    /// <summary>
    /// A shipment's status alongside every one of its transfers', as everything currently in
    /// LTS_Integration says they should be right now.
    /// </summary>
    public sealed record ShipmentStatusSnapshot(
        TrackingStatus ShipmentStatus, IReadOnlyDictionary<string, TrackingStatus> TransferStatuses);

    /// <summary>
    /// What LTS_Shipments.CurrentStatus and every one of its transfers' LTS_ShipmentTransfers.CurrentStatus
    /// should hold right now, derived fresh from the shipment's own dates and its transfers' - the
    /// single source of truth both IntegrationMilestoneService (persisting it immediately after an
    /// edit made through the app) and ShipmentStatusReconciler (catching up on rows changed some
    /// other way - e.g. a future supplier/warehouse integration writing straight into
    /// LTS_ShipmentTransferDates, which the app never sees happen) compute from. Both a shipment
    /// and its transfers are persisted together, from the same snapshot, so the two can never end
    /// up disagreeing with each other in the database the way LTS_ShipmentTransfers.CurrentStatus
    /// alone used to sit unwritten and stale.
    ///
    /// <paramref name="pendingShipmentDates"/>/<paramref name="pendingTransferDates"/> let a
    /// caller that already holds this batch's own tracked (possibly not-yet-saved) date entities
    /// supply them directly, so a date just edited in the same call is accounted for immediately
    /// instead of being missed by a query that can't see an unsaved row. Pass null for both when
    /// there is no in-flight batch to consider - a plain fresh read of what's already committed.
    /// </summary>
    public static async Task<ShipmentStatusSnapshot> ComputeStatusesAsync(
        LtsIntegrationDbContext db,
        string referenceNo,
        IReadOnlyDictionary<string, LtsIntegrationShipmentDate>? pendingShipmentDates,
        IReadOnlyDictionary<string, LtsIntegrationShipmentTransferDate>? pendingTransferDates,
        CancellationToken cancellationToken)
    {
        var shipmentDate = pendingShipmentDates?.GetValueOrDefault(referenceNo)
            ?? await db.ShipmentDates.AsNoTracking()
                .FirstOrDefaultAsync(d => d.ReferenceNo == referenceNo, cancellationToken);

        var milestoneStatus = MilestoneStatus(shipmentDate);

        var transferNos = await db.ShipmentTransfers.AsNoTracking()
            .Where(t => t.ReferenceNo == referenceNo)
            .Select(t => t.TransferNo)
            .ToListAsync(cancellationToken);

        if (transferNos.Count == 0)
        {
            return new ShipmentStatusSnapshot(milestoneStatus, new Dictionary<string, TrackingStatus>());
        }

        var missing = pendingTransferDates is null
            ? transferNos
            : transferNos.Where(t => !pendingTransferDates.ContainsKey(t)).ToList();

        var storedDates = missing.Count == 0
            ? []
            : await db.ShipmentTransferDates.AsNoTracking()
                .Where(d => missing.Contains(d.TransferNo))
                .ToListAsync(cancellationToken);

        var boxes = await db.Boxes.AsNoTracking()
            .Where(b => transferNos.Contains(b.TransferNo))
            .ToListAsync(cancellationToken);

        var transferStatuses = transferNos.ToDictionary(transferNo => transferNo, transferNo =>
        {
            var d = pendingTransferDates?.GetValueOrDefault(transferNo)
                ?? storedDates.FirstOrDefault(x => x.TransferNo == transferNo);
            var transferBoxes = boxes.Where(b => b.TransferNo == transferNo).ToList();

            return TransferStatus(milestoneStatus,
                d?.CrossdockDepartureDate, d?.PlannedStoreArrivalDate, d?.StoreArrivalDate,
                BoxMilestone(transferBoxes, b => b.PreAcceptanceDate),
                BoxMilestone(transferBoxes, b => b.AcceptanceDate));
        });

        var breakdown = transferStatuses.Values
            .GroupBy(s => s)
            .Select(g => new TransferStatusCount(g.Key, g.Count()))
            .ToList();

        var shipmentStatus = AggregateShipmentStatus(milestoneStatus, breakdown);

        return new ShipmentStatusSnapshot(shipmentStatus, transferStatuses);
    }
}
