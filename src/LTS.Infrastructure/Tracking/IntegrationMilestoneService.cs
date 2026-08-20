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
/// Writes shipment-level milestone dates into LTS_Integration's LTS_ShipmentDates, upserting by
/// ReferenceNo, and keeps LTS_Shipments.CurrentStatus/Performance in step with them. Transfer-level
/// dates are not handled here yet - see IntegrationShipmentQueryService for the read side these
/// dates feed.
///
/// Deliberately smaller than the old MilestoneService: CurrentStatus is derived the same way
/// (the furthest milestone reached), but Performance cannot be scored against a KPI target -
/// LTS_Integration has no domain Shipment/KPI target model, and KPI is out of scope here - so it
/// only ever moves between NotStarted and NoTarget. No audit trail yet either. Chronology/
/// future-date validation is kept, since it is cheap and independent of KPI.
/// </summary>
public sealed class IntegrationMilestoneService(
    IDbContextFactory<LtsIntegrationDbContext> dbFactory, IClock clock) : IIntegrationMilestoneService
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

        foreach (var change in requested.Where(c => MilestoneCatalog.Get(c.Type).Scope == MilestoneScope.Transfer))
        {
            errors.Add(new MilestoneError(change.Reference, change.Type,
                "Transfer dates are not yet supported for this shipment."));
        }

        var shipmentChanges = requested
            .Where(c => MilestoneCatalog.Get(c.Type).Scope == MilestoneScope.Shipment)
            .ToList();

        if (shipmentChanges.Count == 0)
        {
            return new MilestoneApplyResult(applied, unchanged, errors);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

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

                var current = GetDate(date, change.Type);
                if (current == change.Date)
                {
                    unchanged++;
                    continue;
                }

                if (change.Date is { } newDate && Validate(newDate, change.Type, t => GetDate(date, t)) is { } validationError)
                {
                    errors.Add(new MilestoneError(change.Reference, change.Type, validationError));
                    continue;
                }

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

                var (status, _) = TrackingStatusCalculator.ForShipment(t => GetDate(date, t));
                shipment.CurrentStatus = status.ToDisplay();
                shipment.Performance = DerivePerformance(date).ToDisplay();
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new MilestoneApplyResult(applied, unchanged, errors);
    }

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
        var prerequisite = MilestoneCatalog.ShipmentMilestones
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
        var rawId = await db.Countries.AsNoTracking()
            .Where(c => c.CustomerCode == customerCode)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return rawId is { } id ? IntegrationCountryId.ToAppId(id) : null;
    }

    /// <summary>
    /// Without a KPI target to score against, Performance can only say whether the shipment has
    /// started moving, not whether it is on time: NotStarted while every shipment milestone date
    /// is empty, NoTarget as soon as any of them has a date.
    /// </summary>
    private static PerformanceStatus DerivePerformance(LtsIntegrationShipmentDate date) =>
        MilestoneCatalog.ShipmentMilestones.Any(m => GetDate(date, m.Type) is not null)
            ? PerformanceStatus.NoTarget
            : PerformanceStatus.NotStarted;

    private static DateOnly? GetDate(LtsIntegrationShipmentDate d, MilestoneType type) => type switch
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
}
