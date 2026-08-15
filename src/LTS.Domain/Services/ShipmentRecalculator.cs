using LTS.Domain.Entities;
using LTS.Domain.Kpi;

namespace LTS.Domain.Services;

/// <summary>
/// Brings a shipment's derived fields back in line with its dates: current status, KPI
/// performance and the transfer rollups. Called after every write — manual, Excel or
/// integration — so the grids never sort on a stale value.
/// </summary>
public static class ShipmentRecalculator
{
    /// <summary>
    /// Recalculates the shipment and every transfer passed with it.
    /// </summary>
    /// <param name="shipment">
    /// The shipment. Its <see cref="Shipment.LoadingPoint"/> and <see cref="Shipment.Transfers"/>
    /// should be loaded; anything missing simply narrows what can be scored.
    /// </param>
    /// <param name="resolver">KPI targets to score against.</param>
    /// <param name="today">Date that running steps are measured against.</param>
    /// <param name="evaluator">Optional evaluator, for a non-calendar day counter.</param>
    public static void Recalculate(
        Shipment shipment,
        KpiTargetResolver resolver,
        DateOnly today,
        KpiEvaluator? evaluator = null)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentNullException.ThrowIfNull(resolver);

        evaluator ??= new KpiEvaluator();

        // Targets are read as of the shipment's own dates, so revising a KPI does not re-score
        // journeys that already happened.
        var key = new KpiLookupKey(
            shipment.ExportTypeId,
            shipment.LoadingPoint?.CountryCode,
            shipment.ArrivalCountryId,
            shipment.LoadingDate ?? shipment.InvoiceDate);

        var targets = resolver.For(key);

        var (status, statusDate) = TrackingStatusCalculator.ForShipment(shipment);
        shipment.CurrentStatus = status;
        shipment.CurrentStatusDate = statusDate;
        shipment.Performance = evaluator
            .Evaluate(shipment.GetMilestoneDates(), targets, today, KpiStepCatalog.ShipmentSteps)
            .Overall;

        foreach (var transfer in shipment.Transfers)
        {
            var (transferStatus, transferStatusDate) = TrackingStatusCalculator.ForTransfer(transfer, shipment);
            transfer.CurrentStatus = transferStatus;
            transfer.CurrentStatusDate = transferStatusDate;

            // Scored across the whole journey, not just the store leg: a store delivery held up
            // by late customs is late for the store, and the Transfers grid has to say so.
            transfer.Performance = evaluator
                .Evaluate(transfer.GetMilestoneDates(shipment), targets, today)
                .Overall;
        }

        RecalculateRollups(shipment);
    }

    /// <summary>
    /// Refreshes the transfer/box/item counts shown on the Shipments grid. Only meaningful once
    /// the transfers are loaded, so an unloaded collection leaves the stored values alone.
    /// </summary>
    public static void RecalculateRollups(Shipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        if (shipment.Transfers.Count == 0)
        {
            return;
        }

        shipment.TransferCount = shipment.Transfers.Count;
        shipment.TotalBoxes = shipment.Transfers.Sum(t => t.TotalBoxes);
        shipment.TotalItems = shipment.Transfers.Sum(t => t.TotalItems);
    }
}
