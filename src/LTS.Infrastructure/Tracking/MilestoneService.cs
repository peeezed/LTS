using LTS.Application.Abstractions;
using LTS.Application.Security;
using LTS.Application.Tracking;
using LTS.Domain.Entities;
using LTS.Domain.Enums;
using LTS.Domain.Milestones;
using LTS.Domain.Services;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Tracking;

/// <summary>
/// Writes milestone dates. Every path into the system — the details form, the Excel upload and
/// the integration poller — comes through here, which is what keeps permissions, auditing and
/// recalculation consistent between them.
/// </summary>
public sealed class MilestoneService(
    LtsDbContext db,
    IKpiTargetProvider kpiTargets,
    ICurrentUser currentUser,
    IClock clock) : IMilestoneService
{
    /// <summary>
    /// Dates are compared against the server's UTC date, so a country a day ahead is not told
    /// its perfectly ordinary entry is in the future.
    /// </summary>
    private static readonly int FutureToleranceDays = 1;

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

        var touched = new HashSet<Shipment>();

        foreach (var group in shipmentChanges.GroupBy(c => c.Reference, StringComparer.OrdinalIgnoreCase))
        {
            var shipment = await FindShipmentAsync(group.Key, cancellationToken);
            if (shipment is null)
            {
                errors.AddRange(group.Select(c => new MilestoneError(c.Reference, c.Type,
                    $"No shipment found with reference or invoice number '{c.Reference}'.")));
                continue;
            }

            foreach (var change in group)
            {
                var outcome = await ApplyOneAsync(
                    change, shipment, MilestoneScope.Shipment, shipment.Id,
                    shipment.GetMilestoneDate, shipment.SetMilestoneDate,
                    options, permissions, errors, cancellationToken);

                if (outcome is Outcome.Applied) applied++;
                else if (outcome is Outcome.Unchanged) unchanged++;
            }

            touched.Add(shipment);
        }

        foreach (var group in transferChanges.GroupBy(c => c.Reference, StringComparer.OrdinalIgnoreCase))
        {
            var transfer = await FindTransferAsync(group.Key, cancellationToken);
            if (transfer?.Shipment is null)
            {
                errors.AddRange(group.Select(c => new MilestoneError(c.Reference, c.Type,
                    $"No transfer found with number '{c.Reference}'.")));
                continue;
            }

            // Chronology checks on a transfer date have to see the shipment's dates too: crossdock
            // departure must follow crossdock arrival, which lives on the shipment.
            var shipment = transfer.Shipment;
            DateOnly? Read(MilestoneType type) =>
                MilestoneCatalog.Get(type).Scope == MilestoneScope.Transfer
                    ? transfer.GetMilestoneDate(type)
                    : shipment.GetMilestoneDate(type);

            foreach (var change in group)
            {
                var outcome = await ApplyOneAsync(
                    change, transfer.Shipment, MilestoneScope.Transfer, transfer.Id,
                    Read, transfer.SetMilestoneDate,
                    options, permissions, errors, cancellationToken);

                if (outcome is Outcome.Applied) applied++;
                else if (outcome is Outcome.Unchanged) unchanged++;
            }

            touched.Add(transfer.Shipment);
        }

        // Status and KPI scores only move when a value actually changed.
        if (applied > 0)
        {
            var resolver = await kpiTargets.GetResolverAsync(cancellationToken);
            foreach (var shipment in touched)
            {
                ShipmentRecalculator.Recalculate(shipment, resolver, clock.Today);
            }
        }

        // Saved whenever anything is pending, not only when a date moved: an incoming value that
        // was deliberately not applied still leaves an audit row explaining why.
        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new MilestoneApplyResult(applied, unchanged, errors);
    }

    private enum Outcome { Applied, Unchanged, Rejected }

    private async Task<Outcome> ApplyOneAsync(
        MilestoneChange change,
        Shipment shipment,
        MilestoneScope scope,
        int entityId,
        Func<MilestoneType, DateOnly?> read,
        Action<MilestoneType, DateOnly?> write,
        MilestoneApplyOptions options,
        UserPermissions permissions,
        List<MilestoneError> errors,
        CancellationToken cancellationToken)
    {
        if (options.EnforcePermissions)
        {
            if (!IsInScope(shipment, permissions))
            {
                errors.Add(new MilestoneError(change.Reference, change.Type,
                    "You do not have access to this shipment."));
                return Outcome.Rejected;
            }

            if (!permissions.CanEditMilestone(change.Type, shipment.ArrivalCountryId))
            {
                errors.Add(new MilestoneError(change.Reference, change.Type,
                    $"You are not allowed to enter '{MilestoneCatalog.DisplayName(change.Type)}'."));
                return Outcome.Rejected;
            }
        }

        var current = read(change.Type);
        if (current == change.Date)
        {
            return Outcome.Unchanged;
        }

        if (change.Date is { } date && Validate(date, change.Type, read) is { } validationError)
        {
            errors.Add(new MilestoneError(change.Reference, change.Type, validationError));
            return Outcome.Rejected;
        }

        // Sources configured to defer to people leave a hand-entered value in place, but the
        // incoming value is still audited so the disagreement is visible.
        if (options.ManualOverrideWins && current is not null &&
            await WasEnteredByHandAsync(scope, entityId, change.Type, cancellationToken))
        {
            db.MilestoneAudits.Add(BuildAudit(scope, entityId, shipment.Id, change, current, options,
                $"Ignored: kept the manually entered value. {options.Note}".Trim()));
            return Outcome.Unchanged;
        }

        db.MilestoneAudits.Add(BuildAudit(scope, entityId, shipment.Id, change, current, options, options.Note));
        write(change.Type, change.Date);

        return Outcome.Applied;
    }

    /// <summary>
    /// Rejects the two mistakes that actually happen: a date typed for an event that has not
    /// occurred, and a date that lands before the milestone it must follow (usually a wrong year).
    /// </summary>
    private string? Validate(DateOnly date, MilestoneType type, Func<MilestoneType, DateOnly?> read)
    {
        var definition = MilestoneCatalog.Get(type);

        // A planned arrival is meant to be in the future; a recorded event is not.
        if (!definition.IsPlanned && date > clock.Today.AddDays(FutureToleranceDays))
        {
            return $"'{definition.DisplayName}' cannot be in the future.";
        }

        var previous = MilestoneCatalog.All
            .Where(d => d.Sequence < definition.Sequence && !d.IsPlanned)
            .Select(d => new { d.DisplayName, Date = read(d.Type) })
            .LastOrDefault(x => x.Date is not null);

        return previous is not null && date < previous.Date
            ? $"'{definition.DisplayName}' ({date:yyyy-MM-dd}) cannot be before '{previous.DisplayName}' ({previous.Date:yyyy-MM-dd})."
            : null;
    }

    private static bool IsInScope(Shipment shipment, UserPermissions permissions)
    {
        if (!permissions.HasCountry(shipment.ArrivalCountryId))
        {
            return false;
        }

        if (!permissions.IsPartnerScoped)
        {
            return true;
        }

        return permissions.UserType == UserType.Broker
            ? shipment.BrokerId == permissions.PartnerId
            : shipment.LogisticsCompanyId == permissions.PartnerId;
    }

    private async Task<bool> WasEnteredByHandAsync(
        MilestoneScope scope, int entityId, MilestoneType type, CancellationToken cancellationToken)
    {
        var lastSource = await db.MilestoneAudits
            .Where(a => a.Scope == scope && a.EntityId == entityId && a.MilestoneType == type)
            .OrderByDescending(a => a.ChangedAt)
            .Select(a => (MilestoneSource?)a.Source)
            .FirstOrDefaultAsync(cancellationToken);

        return lastSource is MilestoneSource.Manual or MilestoneSource.ExcelUpload;
    }

    private MilestoneAudit BuildAudit(
        MilestoneScope scope,
        int entityId,
        int shipmentId,
        MilestoneChange change,
        DateOnly? oldValue,
        MilestoneApplyOptions options,
        string? note) => new()
        {
            Scope = scope,
            EntityId = entityId,
            ShipmentId = shipmentId,
            MilestoneType = change.Type,
            OldValue = oldValue,
            NewValue = change.Date,
            Source = options.Source,
            UserId = currentUser.UserId,
            UserName = currentUser.UserName,
            PartnerId = currentUser.PartnerId,
            IntegrationRunId = options.IntegrationRunId,
            ChangedAt = clock.UtcNow,
            Note = string.IsNullOrWhiteSpace(note) ? null : note
        };

    private Task<Shipment?> FindShipmentAsync(string reference, CancellationToken cancellationToken) =>
        db.Shipments
            .Include(s => s.LoadingPoint)
            .Include(s => s.Transfers)
            .FirstOrDefaultAsync(s => s.ReferenceNo == reference || s.InvoiceNo == reference, cancellationToken);

    private Task<Transfer?> FindTransferAsync(string transferNo, CancellationToken cancellationToken) =>
        db.Transfers
            .Include(t => t.Shipment!).ThenInclude(s => s.LoadingPoint)
            .Include(t => t.Shipment!).ThenInclude(s => s.Transfers)
            .FirstOrDefaultAsync(t => t.TransferNo == transferNo, cancellationToken);
}
