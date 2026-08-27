using LTS.Application.Abstractions;
using LTS.Application.Security;
using LTS.Application.Tracking;
using LTS.Domain.Enums;
using LTS.Domain.Milestones;
using LTS.Domain.Services;
using LTS.Infrastructure.Persistence;
using LTS.Infrastructure.Reference;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Tracking;

/// <summary>
/// Writes milestone dates into LTS_Integration: shipment-level ones into LTS_ShipmentDates
/// (upserting by ReferenceNo), transfer-level ones into LTS_ShipmentTransferDates (upserting by
/// TransferNo). Every shipment touched by a batch - whether directly, via a shipment-level
/// change, or indirectly, via one of its transfers - gets both LTS_Shipments.CurrentStatus and
/// every one of its transfers' LTS_ShipmentTransfers.CurrentStatus recomputed and persisted at
/// the end via ShipmentStatusAggregator, the same logic IntegrationShipmentQueryService uses to
/// compute the same values for display: the shipment capped at AtCrossdock from its own
/// milestones, then InTransitToStore/ArrivedAtStore once its transfers move further; each
/// transfer seeded from that same floor, then advancing on its own dates. Both LTS_Shipments.Performance
/// and every touched transfer's own LTS_ShipmentTransfers.Performance are scored via
/// IntegrationKpiCalculator - the shipment's still folds in every transfer's XDock leg; a
/// transfer's own value is the worst of just its own {XDock, Local Transportation} legs.
///
/// Every applied change writes an LTS_MilestoneAudit row (old value, new value, source, who),
/// mirroring the old MilestoneService's audit trail - except there is no ManualOverrideWins
/// protection here yet, since nothing that currently calls this service ever sets it (no feed
/// writes milestone dates today, only attributes).
/// </summary>
public sealed class IntegrationMilestoneService(
    IDbContextFactory<LtsIntegrationDbContext> dbFactory, IClock clock, ICurrentUser currentUser) : IIntegrationMilestoneService
{
    private const int FutureToleranceDays = 1;

    public async Task<MilestoneApplyResult> ApplyAsync(
        IEnumerable<MilestoneChange> changes,
        MilestoneApplyOptions options,
        UserPermissions permissions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(permissions);

        var requested = changes.ToList();
        if (requested.Count == 0)
        {
            return MilestoneApplyResult.Empty;
        }

        var errors = new List<MilestoneError>();
        var applied = 0;
        var unchanged = 0;

        var shipmentChanges = requested
            .Where(c => MilestoneCatalog.Get(c.Type).Scope == MilestoneScope.Shipment)
            .ToList();

        var transferChanges = requested
            .Where(c => MilestoneCatalog.Get(c.Type).Scope == MilestoneScope.Transfer)
            .ToList();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        // Shipments touched by this batch, directly or via one of their transfers - each gets its
        // CurrentStatus recomputed once, after every change below has been applied, rather than
        // inline per loop: a shipment-scope edit must not blindly overwrite a status a transfer
        // has since carried past AtCrossdock, and a transfer-scope edit needs the shipment's own
        // (possibly just-edited) dates as its floor. Keyed by ReferenceNo so a shipment touched by
        // both loops in the same batch is only recomputed once. The pending dictionaries hold the
        // exact tracked entities already read/created below, so recomputation sees this batch's
        // own not-yet-saved changes (a brand new date row included) without a second round trip.
        var touchedShipments = new Dictionary<string, LtsIntegrationShipment>(StringComparer.OrdinalIgnoreCase);
        var pendingShipmentDates = new Dictionary<string, LtsIntegrationShipmentDate>(StringComparer.OrdinalIgnoreCase);
        var pendingTransferDates = new Dictionary<string, LtsIntegrationShipmentTransferDate>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in shipmentChanges.GroupBy(c => c.Reference, StringComparer.OrdinalIgnoreCase))
        {
            var shipment = await db.Shipments
                .FirstOrDefaultAsync(s => s.ReferenceNo == group.Key || s.InvoiceNo == group.Key, cancellationToken);

            if (shipment is null)
            {
                errors.AddRange(group.Select(c => new MilestoneError(c.Reference, c.Type,
                    $"No shipment found with reference or invoice number '{c.Reference}'.")));
                continue;
            }

            int? countryId = null;
            if (options.EnforcePermissions)
            {
                countryId = await ResolveCountryIdAsync(db, shipment.CustomerCode, cancellationToken);

                var inScope = countryId is not null
                    && permissions.HasCountry(countryId.Value)
                    && await IsPartnerInScopeAsync(db, shipment, permissions, cancellationToken);

                if (!inScope)
                {
                    errors.AddRange(group.Select(c => new MilestoneError(c.Reference, c.Type,
                        "You do not have access to this shipment.")));
                    continue;
                }
            }

            var date = await db.ShipmentDates.FirstOrDefaultAsync(
                d => d.ReferenceNo == shipment.ReferenceNo, cancellationToken);

            var isNew = date is null;
            date ??= new LtsIntegrationShipmentDate { ReferenceNo = shipment.ReferenceNo };

            var changedThisShipment = false;

            foreach (var change in group)
            {
                if (options.EnforcePermissions && !permissions.CanEditMilestone(change.Type, countryId!.Value))
                {
                    errors.Add(new MilestoneError(change.Reference, change.Type,
                        $"You are not allowed to enter '{MilestoneCatalog.DisplayName(change.Type)}'."));
                    continue;
                }

                var current = ShipmentStatusAggregator.GetDate(date, change.Type);
                if (current == change.Date)
                {
                    unchanged++;
                    continue;
                }

                if (change.Date is { } newDate && Validate(newDate, change.Type, t => ShipmentStatusAggregator.GetDate(date, t)) is { } validationError)
                {
                    errors.Add(new MilestoneError(change.Reference, change.Type, validationError));
                    continue;
                }

                RecordAudit(db, options, shipment.ReferenceNo, null, change.Type, current, change.Date);
                SetDate(date, change.Type, change.Date);
                changedThisShipment = true;
                applied++;
            }

            if (changedThisShipment)
            {
                if (isNew)
                {
                    db.ShipmentDates.Add(date);
                }

                pendingShipmentDates[shipment.ReferenceNo] = date;
                touchedShipments[shipment.ReferenceNo] = shipment;
            }
        }

        foreach (var group in transferChanges.GroupBy(c => c.Reference, StringComparer.OrdinalIgnoreCase))
        {
            var transfer = await db.ShipmentTransfers
                .FirstOrDefaultAsync(t => t.TransferNo == group.Key, cancellationToken);

            if (transfer is null)
            {
                errors.AddRange(group.Select(c => new MilestoneError(c.Reference, c.Type,
                    $"No transfer found with number '{c.Reference}'.")));
                continue;
            }

            var shipment = await db.Shipments
                .FirstOrDefaultAsync(s => s.ReferenceNo == transfer.ReferenceNo, cancellationToken);

            if (shipment is null)
            {
                errors.AddRange(group.Select(c => new MilestoneError(c.Reference, c.Type,
                    $"No shipment found for transfer '{c.Reference}'.")));
                continue;
            }

            int? countryId = null;
            if (options.EnforcePermissions)
            {
                countryId = await ResolveCountryIdAsync(db, shipment.CustomerCode, cancellationToken);

                var inScope = countryId is not null
                    && permissions.HasCountry(countryId.Value)
                    && await IsPartnerInScopeAsync(db, shipment, permissions, cancellationToken);

                if (!inScope)
                {
                    errors.AddRange(group.Select(c => new MilestoneError(c.Reference, c.Type,
                        "You do not have access to this shipment.")));
                    continue;
                }
            }

            var shipmentDate = await db.ShipmentDates.FirstOrDefaultAsync(
                d => d.ReferenceNo == shipment.ReferenceNo, cancellationToken);

            var transferDate = await db.ShipmentTransferDates.FirstOrDefaultAsync(
                d => d.TransferNo == transfer.TransferNo, cancellationToken);

            var isNew = transferDate is null;
            transferDate ??= new LtsIntegrationShipmentTransferDate { TransferNo = transfer.TransferNo };

            // Chronology has to see the shipment's own dates too - crossdock departure must
            // follow crossdock arrival, which lives on the shipment, not the transfer.
            DateOnly? Read(MilestoneType type) =>
                MilestoneCatalog.Get(type).Scope == MilestoneScope.Transfer
                    ? GetTransferDate(transferDate, type)
                    : shipmentDate is null ? null : ShipmentStatusAggregator.GetDate(shipmentDate, type);

            var changedThisTransfer = false;

            foreach (var change in group)
            {
                if (options.EnforcePermissions && !permissions.CanEditMilestone(change.Type, countryId!.Value))
                {
                    errors.Add(new MilestoneError(change.Reference, change.Type,
                        $"You are not allowed to enter '{MilestoneCatalog.DisplayName(change.Type)}'."));
                    continue;
                }

                var current = GetTransferDate(transferDate, change.Type);
                if (current == change.Date)
                {
                    unchanged++;
                    continue;
                }

                if (change.Date is { } newDate && Validate(newDate, change.Type, Read) is { } validationError)
                {
                    errors.Add(new MilestoneError(change.Reference, change.Type, validationError));
                    continue;
                }

                RecordAudit(db, options, shipment.ReferenceNo, transfer.TransferNo, change.Type, current, change.Date);
                SetTransferDate(transferDate, change.Type, change.Date);
                changedThisTransfer = true;
                applied++;
            }

            if (changedThisTransfer)
            {
                if (isNew)
                {
                    db.ShipmentTransferDates.Add(transferDate);
                }

                pendingTransferDates[transfer.TransferNo] = transferDate;
                touchedShipments[shipment.ReferenceNo] = shipment;
            }
        }

        var kpiTargets = touchedShipments.Count == 0
            ? []
            : await db.KpiTargets.AsNoTracking().Where(t => t.IsActive).ToListAsync(cancellationToken);

        foreach (var shipment in touchedShipments.Values)
        {
            await RecomputeShipmentStatusAsync(db, shipment, pendingShipmentDates, pendingTransferDates, cancellationToken);
            await RecomputeKpiAsync(db, shipment, pendingShipmentDates, kpiTargets, clock.Today, cancellationToken);
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new MilestoneApplyResult(applied, unchanged, errors);
    }

    /// <summary>
    /// Standalone entry point for callers outside a milestone-date batch (e.g.
    /// ExportAttributeFeedRunner, after backfilling a shipment's KPI-scoping attributes) - reuses
    /// the same private RecomputeKpiAsync the batch path above calls, just with no pending dates to
    /// reuse, so there is exactly one implementation of the recompute sequence either way.
    /// </summary>
    public async Task RecomputeKpiForShipmentAsync(string referenceNo, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.ReferenceNo == referenceNo, cancellationToken);
        if (shipment is null)
        {
            return;
        }

        var targets = await db.KpiTargets.AsNoTracking().Where(t => t.IsActive).ToListAsync(cancellationToken);
        var noPendingDates = new Dictionary<string, LtsIntegrationShipmentDate>(StringComparer.OrdinalIgnoreCase);

        await RecomputeKpiAsync(db, shipment, noPendingDates, targets, clock.Today, cancellationToken);

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Recomputes and persists one shipment's CurrentStatus, and every one of its transfers'
    /// LTS_ShipmentTransfers.CurrentStatus alongside it, via the shared
    /// ShipmentStatusAggregator.ComputeStatusesAsync (also used by ShipmentStatusReconciler) -
    /// every transfer is refreshed, not just ones this batch directly touched, since a
    /// shipment-scope change (e.g. Crossdock Arrival) shifts the floor every transfer is seeded
    /// from. Passes this batch's pending date entities so a date just edited in this same call is
    /// accounted for immediately rather than missed by a fresh read.
    /// </summary>
    private static async Task RecomputeShipmentStatusAsync(
        LtsIntegrationDbContext db,
        LtsIntegrationShipment shipment,
        IReadOnlyDictionary<string, LtsIntegrationShipmentDate> pendingShipmentDates,
        IReadOnlyDictionary<string, LtsIntegrationShipmentTransferDate> pendingTransferDates,
        CancellationToken cancellationToken)
    {
        var snapshot = await ShipmentStatusAggregator.ComputeStatusesAsync(
            db, shipment.ReferenceNo, pendingShipmentDates, pendingTransferDates, cancellationToken);

        shipment.CurrentStatus = snapshot.ShipmentStatus.ToDisplay();

        if (snapshot.TransferStatuses.Count == 0)
        {
            return;
        }

        var transfers = await db.ShipmentTransfers
            .Where(t => t.ReferenceNo == shipment.ReferenceNo)
            .ToListAsync(cancellationToken);

        foreach (var transfer in transfers)
        {
            if (snapshot.TransferStatuses.TryGetValue(transfer.TransferNo, out var status))
            {
                transfer.CurrentStatus = status.ToDisplay();
            }
        }
    }

    /// <summary>
    /// Recomputes and persists this shipment's KPI deadlines and overall Performance, via the
    /// shared IntegrationKpiCalculator. Skipped entirely (Performance left as whatever it already
    /// held) when the shipment's CustomerCode does not resolve to a country - KPI targets are
    /// scoped by country, so there is nothing to compute against without one; this should not
    /// happen in practice, since the same CustomerCode match is relied on everywhere else in the
    /// app too.
    /// </summary>
    private static async Task RecomputeKpiAsync(
        LtsIntegrationDbContext db,
        LtsIntegrationShipment shipment,
        IReadOnlyDictionary<string, LtsIntegrationShipmentDate> pendingShipmentDates,
        IReadOnlyList<LtsIntegrationKpiTarget> kpiTargets,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var countryId = await ResolveRawCountryIdAsync(db, shipment.CustomerCode, cancellationToken);
        if (countryId is null)
        {
            return;
        }

        var date = pendingShipmentDates.GetValueOrDefault(shipment.ReferenceNo)
            ?? await db.ShipmentDates.FirstOrDefaultAsync(d => d.ReferenceNo == shipment.ReferenceNo, cancellationToken);

        IReadOnlyList<LtsIntegrationShipmentTransferDate> transferDates = [];

        if (date is not null)
        {
            transferDates = await IntegrationKpiCalculator.RecomputeDeadlinesAsync(
                db, shipment, date, countryId.Value, kpiTargets, cancellationToken);
        }

        shipment.Performance = IntegrationKpiCalculator
            .EvaluatePerformance(shipment, date, transferDates, today)
            .ToDisplay();

        var scope = IntegrationKpiCalculator.ScopeOf(shipment);
        var transfers = await db.ShipmentTransfers
            .Where(t => t.ReferenceNo == shipment.ReferenceNo)
            .ToListAsync(cancellationToken);

        foreach (var transfer in transfers)
        {
            var transferDate = transferDates.FirstOrDefault(d => d.TransferNo == transfer.TransferNo)
                ?? await db.ShipmentTransferDates.FirstOrDefaultAsync(d => d.TransferNo == transfer.TransferNo, cancellationToken);

            transfer.Performance = IntegrationKpiCalculator
                .EvaluateTransferPerformance(scope, date?.CrossdockArrivalDate, transferDate, today)
                .ToDisplay();
        }
    }

    /// <summary>
    /// Records one applied change to LTS_MilestoneAudit - added to the same tracked db so it
    /// saves together with the actual date write in the batch's single SaveChangesAsync call.
    /// transferNo is null for a shipment-scope change.
    /// </summary>
    private void RecordAudit(
        LtsIntegrationDbContext db, MilestoneApplyOptions options, string referenceNo, string? transferNo,
        MilestoneType type, DateOnly? oldValue, DateOnly? newValue) =>
        db.MilestoneAudits.Add(new LtsIntegrationMilestoneAudit
        {
            ReferenceNo = referenceNo,
            TransferNo = transferNo,
            MilestoneType = type,
            OldValue = oldValue,
            NewValue = newValue,
            Source = options.Source,
            UserId = currentUser.UserId,
            UserName = currentUser.UserName,
            PartnerId = currentUser.PartnerId,
            ChangedAt = clock.UtcNow,
            Note = options.Note
        });

    /// <summary>Rejects a date typed for an event that has not occurred, or one out of order.</summary>
    private string? Validate(DateOnly date, MilestoneType type, Func<MilestoneType, DateOnly?> read)
    {
        var definition = MilestoneCatalog.Get(type);

        if (!definition.IsPlanned && date > clock.Today.AddDays(FutureToleranceDays))
        {
            return $"'{definition.DisplayName}' cannot be in the future.";
        }

        // Each owner enters their own dates in order - the logistics company's own chain
        // (Loading through Arrival To Target Country) and the broker's own chain (Customs Start,
        // Customs End) are each gated on the previous date in that same chain, but not on each
        // other: a broker can enter Customs Start before the logistics company has entered
        // Arrival To Target Country. Crossdock Arrival has no owner-chain prerequisite - it is
        // entered manually or arrives from the in-house service independently of everything else.
        // Warehouse's chain spans both scopes (Crossdock Arrival on the shipment, then Crossdock
        // Departure and Planned/Store Arrival on the transfer), so this searches every milestone,
        // not just shipment ones - which changes nothing for the other owners, since none of them
        // own a transfer-scope milestone.
        var prerequisite = MilestoneCatalog.All
            .Where(d => d.Owner == definition.Owner && d.Sequence < definition.Sequence)
            .OrderByDescending(d => d.Sequence)
            .FirstOrDefault();

        if (prerequisite is null)
        {
            return null;
        }

        var prerequisiteDate = read(prerequisite.Type);

        if (prerequisiteDate is null)
        {
            return $"'{prerequisite.DisplayName}' must be entered before '{definition.DisplayName}'.";
        }

        return date < prerequisiteDate
            ? $"'{definition.DisplayName}' ({date:yyyy-MM-dd}) cannot be before '{prerequisite.DisplayName}' ({prerequisiteDate:yyyy-MM-dd})."
            : null;
    }

    /// <summary>
    /// Whether a Broker/LogisticsCompany account may touch this shipment - matched by its
    /// SupplierCompanyCode's Description (via LTS_LogisticsCompanies/LTS_Brokers) against
    /// LTS_Shipments.BrokerCompany/LogisticsCompany, the same way the read side matches it. Always
    /// true for accounts that are not partner-scoped.
    /// </summary>
    private static async Task<bool> IsPartnerInScopeAsync(
        LtsIntegrationDbContext db, LtsIntegrationShipment shipment, UserPermissions permissions, CancellationToken cancellationToken)
    {
        if (!permissions.IsPartnerScoped)
        {
            return true;
        }

        if (permissions.SupplierCompanyCode is not { } code)
        {
            return false;
        }

        var table = permissions.UserType == UserType.Broker ? db.BrokerAttributes : db.LogisticsCompanyAttributes;

        var name = await table.AsNoTracking()
            .Where(a => a.Code == code)
            .Select(a => a.Description)
            .FirstOrDefaultAsync(cancellationToken);

        if (name is null)
        {
            return false;
        }

        return permissions.UserType == UserType.Broker
            ? shipment.BrokerCompany == name
            : shipment.LogisticsCompany == name;
    }

    /// <summary>The offset app-wide id of the country a shipment's CustomerCode resolves to, or null.</summary>
    private static async Task<int?> ResolveCountryIdAsync(
        LtsIntegrationDbContext db, string customerCode, CancellationToken cancellationToken)
    {
        var rawId = await ResolveRawCountryIdAsync(db, customerCode, cancellationToken);
        return rawId is { } id ? IntegrationCountryId.ToAppId(id) : null;
    }

    /// <summary>
    /// The raw LTS_Countries.ID a shipment's CustomerCode resolves to, or null - the id space
    /// LTS_KpiTargets.CountryId is stored in, unlike the app-wide offset id ResolveCountryIdAsync
    /// returns for permission checks.
    /// </summary>
    private static Task<int?> ResolveRawCountryIdAsync(
        LtsIntegrationDbContext db, string customerCode, CancellationToken cancellationToken) =>
        db.Countries.AsNoTracking()
            .Where(c => c.CustomerCode == customerCode)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static void SetDate(LtsIntegrationShipmentDate d, MilestoneType type, DateOnly? value)
    {
        switch (type)
        {
            case MilestoneType.Loading: d.LoadingDate = value; break;
            case MilestoneType.DepartureCustomsClearance: d.CustomsClearanceDate = value; break;
            case MilestoneType.Departure: d.DepartureDate = value; break;
            case MilestoneType.ArrivalToTargetCountry: d.ArrivalDate = value; break;
            case MilestoneType.CustomsStart: d.ArrivalCustomsStartDate = value; break;
            case MilestoneType.CustomsEnd: d.ArrivalCustomsEndDate = value; break;
            case MilestoneType.CrossdockArrival: d.CrossdockArrivalDate = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(type), type, "Not a shipment milestone.");
        }
    }

    /// <summary>
    /// Store Pre Acceptance/Acceptance are not here: they live on LTS_Boxes, one row per box, and
    /// are never entered manually (AllowsManualEntry is false for both, so CanEditMilestone
    /// already rejects them before either of these is called).
    /// </summary>
    private static DateOnly? GetTransferDate(LtsIntegrationShipmentTransferDate d, MilestoneType type) => type switch
    {
        MilestoneType.CrossdockDeparture => d.CrossdockDepartureDate,
        MilestoneType.PlannedStoreArrival => d.PlannedStoreArrivalDate,
        MilestoneType.StoreArrival => d.StoreArrivalDate,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a manually-entered transfer milestone.")
    };

    private static void SetTransferDate(LtsIntegrationShipmentTransferDate d, MilestoneType type, DateOnly? value)
    {
        switch (type)
        {
            case MilestoneType.CrossdockDeparture: d.CrossdockDepartureDate = value; break;
            case MilestoneType.PlannedStoreArrival: d.PlannedStoreArrivalDate = value; break;
            case MilestoneType.StoreArrival: d.StoreArrivalDate = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(type), type, "Not a manually-entered transfer milestone.");
        }
    }
}
