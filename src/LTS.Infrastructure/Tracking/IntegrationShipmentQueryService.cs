using LTS.Application.Abstractions;
using LTS.Application.Security;
using LTS.Application.Tracking;
using LTS.Domain.Enums;
using LTS.Domain.Services;
using LTS.Infrastructure.Persistence;
using LTS.Infrastructure.Reference;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Tracking;

/// <summary>
/// Reads for the tracking pages, sourced from LTS_Integration instead of the app's own database.
/// A shipment's country is resolved by matching LTS_Shipments.CustomerCode against
/// LTS_Countries.CustomerCode - LTS_Integration does not carry a country id on the shipment
/// itself. Every reachable country now comes from LTS_Integration (see CountryContext), so this
/// replaces the old LtsDbContext-backed ShipmentQueryService rather than sitting alongside it.
///
/// Row counts here are expected to be small while the integration is still being onboarded, so
/// filtering/sorting is done in SQL where the flat schema allows it, and the rest (status/
/// performance parsing, box-level rollups) is done in memory on the already-paged result.
/// </summary>
public sealed class IntegrationShipmentQueryService(
    IDbContextFactory<LtsIntegrationDbContext> dbFactory, IClock clock) : IShipmentQueryService
{
    public async Task<PagedResult<ShipmentRow>> GetShipmentsAsync(
        int countryId,
        UserPermissions permissions,
        ShipmentFilter filter,
        GridRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!permissions.HasCountry(countryId))
        {
            return PagedResult<ShipmentRow>.Empty;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var (restricted, partnerName) = await ResolvePartnerFilterAsync(db, permissions, cancellationToken);
        if (restricted && partnerName is null)
        {
            return PagedResult<ShipmentRow>.Empty;
        }

        var countryInfo = await CountryInfoForAsync(db, countryId, cancellationToken);
        if (countryInfo is not { } info)
        {
            return PagedResult<ShipmentRow>.Empty;
        }

        await BackfillArrivalCountryAsync(db, info.CustomerCode, info.CountryName, cancellationToken);

        var query = db.Shipments.AsNoTracking().Where(s => s.CustomerCode == info.CustomerCode);
        query = ApplyPartnerFilter(query, permissions, restricted, partnerName);
        query = await ApplyFilterAsync(db, query, filter, cancellationToken);

        var total = await query.CountAsync(cancellationToken);

        var page = await Sort(query, request)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var references = page.Select(s => s.ReferenceNo).ToList();
        var dates = await ToDictionaryTolerantAsync(
            db.ShipmentDates.AsNoTracking().Where(d => references.Contains(d.ReferenceNo)),
            d => d.ReferenceNo, d => d, cancellationToken);

        var attributes = await ResolveAttributesAsync(db, page, cancellationToken);
        var breakdowns = await TransferStatusBreakdownsAsync(db, page, dates, cancellationToken);

        var rows = page.Select(s =>
        {
            var d = dates.GetValueOrDefault(s.ReferenceNo);
            var transferBreakdown = breakdowns.GetValueOrDefault(s.ReferenceNo, []);
            var shipmentStatus = ShipmentStatusAggregator.AggregateShipmentStatus(
                ShipmentStatusAggregator.MilestoneStatus(d), transferBreakdown);

            return new ShipmentRow
            {
                Id = s.Id,
                ReferenceNo = s.ReferenceNo,
                InvoiceNo = s.InvoiceNo,
                InvoiceDate = s.InvoiceDate,
                ArrivalCountry = s.ArrivalCountry,
                ArrivalCustoms = attributes.ArrivalCustoms.Resolve(s.ArrivalCustoms),
                ExportType = attributes.ExportType.Resolve(s.ExportType),
                TransportType = attributes.TransportType.Resolve(s.TransportType),
                LoadingPoint = attributes.LoadingPoint.Resolve(s.LoadingPoint),
                LoadingCountryCode = null,
                LogisticsCompany = attributes.LogisticsCompany.Resolve(s.LogisticsCompany),
                Broker = attributes.Broker.Resolve(s.BrokerCompany),
                LoadingDate = d?.LoadingDate,
                DepartureCustomsClearanceDate = d?.CustomsClearanceDate,
                DepartureDate = d?.DepartureDate,
                ArrivalToTargetCountryDate = d?.ArrivalDate,
                CustomsStartDate = d?.ArrivalCustomsStartDate,
                CustomsEndDate = d?.ArrivalCustomsEndDate,
                CrossdockArrivalDate = d?.CrossdockArrivalDate,
                TransferCount = s.TotalTransfers ?? 0,
                TotalBoxes = s.TotalBoxes ?? 0,
                TotalItems = s.TotalItems ?? 0,
                TransferStatusBreakdown = transferBreakdown,
                CurrentStatus = shipmentStatus,
                CurrentStatusDate = null,
                Performance = ParsePerformance(s.Performance)
            };
        }).ToList();

        return new PagedResult<ShipmentRow>(rows, total);
    }

    /// <summary>
    /// How each of the given shipments' transfers is spread across statuses, keyed by
    /// ReferenceNo - the Shipments grid's "shipment status stops at crossdock" complement, since
    /// a shipment gives no other visibility into what's happened to it since. Shipments with no
    /// transfers are simply absent from the result. Takes the shipments' own already-loaded
    /// LTS_ShipmentDates so each transfer is seeded from the true milestone-only floor
    /// (ShipmentStatusAggregator.MilestoneStatus) rather than LTS_Shipments.CurrentStatus, which
    /// may already hold an aggregated value once a sibling transfer has advanced it.
    /// </summary>
    private async Task<Dictionary<string, IReadOnlyList<TransferStatusCount>>> TransferStatusBreakdownsAsync(
        LtsIntegrationDbContext db, IReadOnlyList<LtsIntegrationShipment> shipments,
        IReadOnlyDictionary<string, LtsIntegrationShipmentDate> shipmentDates, CancellationToken cancellationToken)
    {
        var references = shipments.Select(s => s.ReferenceNo).ToList();

        var transfers = await db.ShipmentTransfers.AsNoTracking()
            .Where(t => references.Contains(t.ReferenceNo))
            .ToListAsync(cancellationToken);

        if (transfers.Count == 0)
        {
            return [];
        }

        var transferNos = transfers.Select(t => t.TransferNo).ToList();
        var dates = await ToDictionaryTolerantAsync(
            db.ShipmentTransferDates.AsNoTracking().Where(d => transferNos.Contains(d.TransferNo)),
            d => d.TransferNo, d => d, cancellationToken);
        var boxes = await db.Boxes.AsNoTracking()
            .Where(b => transferNos.Contains(b.TransferNo))
            .ToListAsync(cancellationToken);

        var shipmentStatuses = references
            .Distinct()
            .ToDictionary(r => r, r => ShipmentStatusAggregator.MilestoneStatus(shipmentDates.GetValueOrDefault(r)));

        return transfers
            .GroupBy(t => t.ReferenceNo)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var shipmentStatus = shipmentStatuses.GetValueOrDefault(g.Key, TrackingStatus.Created);

                    return (IReadOnlyList<TransferStatusCount>)
                    [
                        .. g.Select(t =>
                            {
                                var d = dates.GetValueOrDefault(t.TransferNo);
                                var transferBoxes = boxes.Where(b => b.TransferNo == t.TransferNo).ToList();

                                return ShipmentStatusAggregator.TransferStatus(shipmentStatus, d?.CrossdockDepartureDate,
                                    d?.PlannedStoreArrivalDate, d?.StoreArrivalDate,
                                    BoxMilestone(transferBoxes, b => b.PreAcceptanceDate),
                                    BoxMilestone(transferBoxes, b => b.AcceptanceDate));
                            })
                            .GroupBy(status => status)
                            .OrderBy(statusGroup => statusGroup.Key)
                            .Select(statusGroup => new TransferStatusCount(statusGroup.Key, statusGroup.Count()))
                    ];
                });
    }

    public async Task<PagedResult<TransferRow>> GetTransfersAsync(
        int countryId,
        UserPermissions permissions,
        ShipmentFilter filter,
        GridRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!permissions.HasCountry(countryId))
        {
            return PagedResult<TransferRow>.Empty;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var (restricted, partnerName) = await ResolvePartnerFilterAsync(db, permissions, cancellationToken);
        if (restricted && partnerName is null)
        {
            return PagedResult<TransferRow>.Empty;
        }

        var countryInfo = await CountryInfoForAsync(db, countryId, cancellationToken);
        if (countryInfo is not { } info)
        {
            return PagedResult<TransferRow>.Empty;
        }

        var query =
            from t in db.ShipmentTransfers.AsNoTracking()
            join s in db.Shipments.AsNoTracking() on t.ReferenceNo equals s.ReferenceNo
            where s.CustomerCode == info.CustomerCode
            select new { Transfer = t, Shipment = s };

        if (restricted)
        {
            query = permissions.UserType == UserType.Broker
                ? query.Where(x => x.Shipment.BrokerCompany == partnerName)
                : query.Where(x => x.Shipment.LogisticsCompany == partnerName);
        }

        if (await ResolveCompanyCodeAsync(db.LogisticsCompanyAttributes, filter.LogisticsCompanyCode, cancellationToken)
            is { } logisticsCompanyName)
        {
            query = query.Where(x => x.Shipment.LogisticsCompany == logisticsCompanyName);
        }

        if (await ResolveCompanyCodeAsync(db.BrokerAttributes, filter.BrokerCode, cancellationToken) is { } brokerName)
        {
            query = query.Where(x => x.Shipment.BrokerCompany == brokerName);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(x =>
                x.Transfer.TransferNo.Contains(term) ||
                x.Shipment.ReferenceNo.Contains(term) ||
                x.Shipment.InvoiceNo.Contains(term));
        }

        // Filters against the stored CurrentStatus text, not the shipment-inheriting status the
        // row itself now displays (see TransferStatus below) - computing the inherited status
        // would mean loading every candidate transfer's dates before paging/filtering, not just
        // the current page's. A transfer without its own Crossdock Departure date yet may
        // therefore not match a status filter that its displayed status would suggest it should.
        if (filter.Statuses is { Count: > 0 })
        {
            var labels = filter.Statuses.Select(v => v.ToDisplay()).ToList();
            query = query.Where(x => labels.Contains(x.Transfer.CurrentStatus));
        }

        if (filter.Performances is { Count: > 0 })
        {
            var labels = filter.Performances.Select(v => v.ToDisplay()).ToList();
            query = query.Where(x => labels.Contains(x.Transfer.Performance));
        }

        if (filter.InvoiceDateFrom is { } from)
        {
            query = query.Where(x => x.Shipment.InvoiceDate >= from);
        }

        if (filter.InvoiceDateTo is { } to)
        {
            query = query.Where(x => x.Shipment.InvoiceDate <= to);
        }

        var total = await query.CountAsync(cancellationToken);

        var descending = request.SortDescending;
        query = request.SortBy switch
        {
            nameof(TransferRow.TransferNo) => descending
                ? query.OrderByDescending(x => x.Transfer.TransferNo) : query.OrderBy(x => x.Transfer.TransferNo),
            nameof(TransferRow.InvoiceNo) => descending
                ? query.OrderByDescending(x => x.Shipment.InvoiceNo) : query.OrderBy(x => x.Shipment.InvoiceNo),
            nameof(TransferRow.DateCreated) => descending
                ? query.OrderByDescending(x => x.Shipment.InvoiceDate) : query.OrderBy(x => x.Shipment.InvoiceDate),
            nameof(TransferRow.Receiver) => descending
                ? query.OrderByDescending(x => x.Transfer.ReceivingStoreCode) : query.OrderBy(x => x.Transfer.ReceivingStoreCode),
            nameof(TransferRow.CurrentStatus) => descending
                ? query.OrderByDescending(x => x.Transfer.CurrentStatus) : query.OrderBy(x => x.Transfer.CurrentStatus),
            nameof(TransferRow.Performance) => descending
                ? query.OrderByDescending(x => x.Transfer.Performance) : query.OrderBy(x => x.Transfer.Performance),
            _ => query.OrderByDescending(x => x.Shipment.InvoiceDate).ThenBy(x => x.Transfer.TransferNo)
        };

        var page = await query.Skip(request.Skip).Take(request.PageSize).ToListAsync(cancellationToken);

        var transferNos = page.Select(x => x.Transfer.TransferNo).ToList();
        var dates = await ToDictionaryTolerantAsync(
            db.ShipmentTransferDates.AsNoTracking().Where(d => transferNos.Contains(d.TransferNo)),
            d => d.TransferNo, d => d, cancellationToken);
        var boxes = await db.Boxes.AsNoTracking()
            .Where(b => transferNos.Contains(b.TransferNo))
            .ToListAsync(cancellationToken);

        // The shipment's own dates, not its (possibly already transfer-aggregated)
        // LTS_Shipments.CurrentStatus - see ShipmentStatusAggregator.MilestoneStatus.
        var shipmentReferences = page.Select(x => x.Shipment.ReferenceNo).Distinct().ToList();
        var shipmentDates = await ToDictionaryTolerantAsync(
            db.ShipmentDates.AsNoTracking().Where(d => shipmentReferences.Contains(d.ReferenceNo)),
            d => d.ReferenceNo, d => d, cancellationToken);

        var rows = page.Select(x =>
        {
            var d = dates.GetValueOrDefault(x.Transfer.TransferNo);
            var transferBoxes = boxes.Where(b => b.TransferNo == x.Transfer.TransferNo).ToList();
            var storePreAcceptance = BoxMilestone(transferBoxes, b => b.PreAcceptanceDate);
            var storeAcceptance = BoxMilestone(transferBoxes, b => b.AcceptanceDate);
            var milestoneStatus = ShipmentStatusAggregator.MilestoneStatus(
                shipmentDates.GetValueOrDefault(x.Shipment.ReferenceNo));

            return new TransferRow
            {
                Id = SyntheticId(x.Transfer.TransferNo),
                ShipmentId = x.Shipment.Id,
                TransferNo = x.Transfer.TransferNo,
                ReferenceNo = x.Shipment.ReferenceNo,
                InvoiceNo = x.Transfer.InvoiceNo ?? x.Shipment.InvoiceNo,
                DateCreated = x.Transfer.DateCreated ?? x.Shipment.InvoiceDate,
                StoreCode = x.Transfer.ReceivingStoreCode,
                StoreName = null,
                CurrentStatus = ShipmentStatusAggregator.TransferStatus(milestoneStatus,
                    d?.CrossdockDepartureDate, d?.PlannedStoreArrivalDate, d?.StoreArrivalDate,
                    storePreAcceptance, storeAcceptance),
                Performance = ParsePerformance(x.Transfer.Performance),
                TotalBoxes = x.Transfer.TotalBoxes ?? 0,
                TotalItems = x.Transfer.TotalItems ?? 0,
                CrossdockDepartureDate = d?.CrossdockDepartureDate,
                PlannedStoreArrivalDate = d?.PlannedStoreArrivalDate,
                StoreArrivalDate = d?.StoreArrivalDate,
                StorePreAcceptanceDate = storePreAcceptance,
                StoreAcceptanceDate = storeAcceptance
            };
        }).ToList();

        return new PagedResult<TransferRow>(rows, total);
    }

    public async Task<ShipmentDetail?> GetShipmentDetailAsync(
        int countryId,
        UserPermissions permissions,
        string reference,
        CancellationToken cancellationToken = default)
    {
        if (!permissions.HasCountry(countryId) || string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        reference = reference.Trim();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var (restricted, partnerName) = await ResolvePartnerFilterAsync(db, permissions, cancellationToken);
        if (restricted && partnerName is null)
        {
            return null;
        }

        var countryInfo = await CountryInfoForAsync(db, countryId, cancellationToken);
        if (countryInfo is not { } info)
        {
            return null;
        }

        await BackfillArrivalCountryAsync(db, info.CustomerCode, info.CountryName, cancellationToken);

        var shipmentQuery = db.Shipments.AsNoTracking()
            .Where(s => s.CustomerCode == info.CustomerCode && (s.ReferenceNo == reference || s.InvoiceNo == reference));
        shipmentQuery = ApplyPartnerFilter(shipmentQuery, permissions, restricted, partnerName);

        var shipment = await shipmentQuery.FirstOrDefaultAsync(cancellationToken);

        if (shipment is null)
        {
            return null;
        }

        var date = await db.ShipmentDates.AsNoTracking()
            .FirstOrDefaultAsync(d => d.ReferenceNo == shipment.ReferenceNo, cancellationToken);

        var transfers = await db.ShipmentTransfers.AsNoTracking()
            .Where(t => t.ReferenceNo == shipment.ReferenceNo)
            .OrderBy(t => t.TransferNo)
            .ToListAsync(cancellationToken);

        var transferNos = transfers.Select(t => t.TransferNo).ToList();
        var transferDates = await ToDictionaryTolerantAsync(
            db.ShipmentTransferDates.AsNoTracking().Where(d => transferNos.Contains(d.TransferNo)),
            d => d.TransferNo, d => d, cancellationToken);
        var boxes = await db.Boxes.AsNoTracking()
            .Where(b => transferNos.Contains(b.TransferNo))
            .ToListAsync(cancellationToken);

        var attributes = await ResolveAttributesAsync(db, [shipment], cancellationToken);
        var milestoneStatus = ShipmentStatusAggregator.MilestoneStatus(date);

        var transferDetails = transfers.Select(t =>
        {
            var td = transferDates.GetValueOrDefault(t.TransferNo);
            var transferBoxes = boxes.Where(b => b.TransferNo == t.TransferNo).ToList();
            var storePreAcceptance = BoxMilestone(transferBoxes, b => b.PreAcceptanceDate);
            var storeAcceptance = BoxMilestone(transferBoxes, b => b.AcceptanceDate);

            return new TransferDetail
            {
                Id = SyntheticId(t.TransferNo),
                TransferNo = t.TransferNo,
                Receiver = t.ReceivingStoreCode ?? string.Empty,
                TotalBoxes = t.TotalBoxes ?? 0,
                TotalItems = t.TotalItems ?? 0,
                CurrentStatus = ShipmentStatusAggregator.TransferStatus(milestoneStatus, td?.CrossdockDepartureDate,
                    td?.PlannedStoreArrivalDate, td?.StoreArrivalDate, storePreAcceptance, storeAcceptance),
                Performance = ParsePerformance(t.Performance),
                Milestones = new Dictionary<MilestoneType, DateOnly?>
                {
                    [MilestoneType.CrossdockDeparture] = td?.CrossdockDepartureDate,
                    [MilestoneType.PlannedStoreArrival] = td?.PlannedStoreArrivalDate,
                    [MilestoneType.StoreArrival] = td?.StoreArrivalDate,
                    [MilestoneType.StorePreAcceptance] = storePreAcceptance,
                    [MilestoneType.StoreAcceptance] = storeAcceptance
                }
            };
        }).ToList();

        var shipmentStatus = ShipmentStatusAggregator.AggregateShipmentStatus(milestoneStatus,
            [.. transferDetails
                .GroupBy(t => t.CurrentStatus)
                .Select(g => new TransferStatusCount(g.Key, g.Count()))]);

        return new ShipmentDetail
        {
            Id = shipment.Id,
            ArrivalCountryId = countryId,
            ReferenceNo = shipment.ReferenceNo,
            InvoiceNo = shipment.InvoiceNo,
            InvoiceDate = shipment.InvoiceDate,
            ArrivalCountry = shipment.ArrivalCountry,
            ArrivalCustoms = attributes.ArrivalCustoms.Resolve(shipment.ArrivalCustoms),
            ExportType = attributes.ExportType.Resolve(shipment.ExportType),
            TransportType = attributes.TransportType.Resolve(shipment.TransportType),
            LoadingPoint = attributes.LoadingPoint.Resolve(shipment.LoadingPoint),
            LogisticsCompany = attributes.LogisticsCompany.Resolve(shipment.LogisticsCompany),
            Broker = attributes.Broker.Resolve(shipment.BrokerCompany),
            CurrentStatus = shipmentStatus,
            Performance = ParsePerformance(shipment.Performance),
            TransferCount = shipment.TotalTransfers ?? 0,
            TotalBoxes = shipment.TotalBoxes ?? 0,
            TotalItems = shipment.TotalItems ?? 0,
            Milestones = new Dictionary<MilestoneType, DateOnly?>
            {
                [MilestoneType.Loading] = date?.LoadingDate,
                [MilestoneType.DepartureCustomsClearance] = date?.CustomsClearanceDate,
                [MilestoneType.Departure] = date?.DepartureDate,
                [MilestoneType.ArrivalToTargetCountry] = date?.ArrivalDate,
                [MilestoneType.CustomsStart] = date?.ArrivalCustomsStartDate,
                [MilestoneType.CustomsEnd] = date?.ArrivalCustomsEndDate,
                [MilestoneType.CrossdockArrival] = date?.CrossdockArrivalDate
            },
            Transfers = transferDetails
        };
    }

    public async Task<InTransitSummary> GetInTransitSummaryAsync(
        int countryId,
        UserPermissions permissions,
        CancellationToken cancellationToken = default)
    {
        if (!permissions.HasCountry(countryId))
        {
            return InTransitSummary.Empty;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var (restricted, partnerName) = await ResolvePartnerFilterAsync(db, permissions, cancellationToken);
        if (restricted && partnerName is null)
        {
            return InTransitSummary.Empty;
        }

        var countryInfo = await CountryInfoForAsync(db, countryId, cancellationToken);
        if (countryInfo is not { } info)
        {
            return InTransitSummary.Empty;
        }

        // "In transit" means the shipment's displayed status (its own milestones, or its
        // transfers' once they've moved further - see AggregateShipmentStatus) hasn't reached
        // ArrivedAtStore yet, the shipment's terminal status once every transfer has reached its
        // store.
        var shipmentsQuery = db.Shipments.AsNoTracking().Where(s => s.CustomerCode == info.CustomerCode);
        shipmentsQuery = ApplyPartnerFilter(shipmentsQuery, permissions, restricted, partnerName);

        var shipments = await shipmentsQuery.ToListAsync(cancellationToken);

        if (shipments.Count == 0)
        {
            return InTransitSummary.Empty;
        }

        var references = shipments.Select(s => s.ReferenceNo).ToList();
        var dates = await ToDictionaryTolerantAsync(
            db.ShipmentDates.AsNoTracking().Where(d => references.Contains(d.ReferenceNo)),
            d => d.ReferenceNo, d => d, cancellationToken);
        var breakdowns = await TransferStatusBreakdownsAsync(db, shipments, dates, cancellationToken);

        var items = shipments
            .Select(s =>
            {
                var d = dates.GetValueOrDefault(s.ReferenceNo);
                var milestoneStatus = ShipmentStatusAggregator.MilestoneStatus(d);

                return new
                {
                    Shipment = s,
                    Status = ShipmentStatusAggregator.AggregateShipmentStatus(milestoneStatus, breakdowns.GetValueOrDefault(s.ReferenceNo, [])),
                    Performance = ParsePerformance(s.Performance),
                    LoadingDate = d?.LoadingDate
                };
            })
            .Where(i => i.Status < TrackingStatus.ArrivedAtStore)
            .ToList();

        if (items.Count == 0)
        {
            return InTransitSummary.Empty;
        }

        var today = clock.Today;

        return new InTransitSummary
        {
            ShipmentCount = items.Count,
            TransferCount = items.Sum(i => i.Shipment.TotalTransfers ?? 0),
            TotalBoxes = items.Sum(i => i.Shipment.TotalBoxes ?? 0),
            TotalItems = items.Sum(i => i.Shipment.TotalItems ?? 0),
            OverdueCount = items.Count(i => i.Performance == PerformanceStatus.Overdue),
            AtRiskCount = items.Count(i => i.Performance == PerformanceStatus.AtRisk),
            ByStatus =
            [
                .. items.GroupBy(i => i.Status)
                    .OrderBy(g => g.Key)
                    .Select(g => new CountBucket<TrackingStatus>(g.Key, g.Key.ToDisplay(), g.Count()))
            ],
            ByPerformance =
            [
                .. items.GroupBy(i => i.Performance)
                    .OrderBy(g => g.Key)
                    .Select(g => new CountBucket<PerformanceStatus>(g.Key, g.Key.ToDisplay(), g.Count()))
            ],
            Aging = AgingBuckets(items.Select(i => i.LoadingDate), today),
            ByLogisticsCompany = PartnerBuckets(items.Select(i => (i.Shipment.LogisticsCompany, i.Performance))),
            ByBroker = PartnerBuckets(items.Select(i => (i.Shipment.BrokerCompany, i.Performance)))
        };
    }

    /// <summary>
    /// Fills in LTS_Shipments.ArrivalCountry for any shipment of this customer that doesn't have
    /// one yet - the integration writes the seven attribute columns independently of the country
    /// match, so a shipment can otherwise land with ArrivalCountry blank even though its country
    /// is already known from CustomerCode. Once a row has a value, it's left alone: the receiving
    /// country is only ever a fallback, never overwritten by a later read.
    /// </summary>
    private static Task BackfillArrivalCountryAsync(
        LtsIntegrationDbContext db, string customerCode, string countryName, CancellationToken cancellationToken) =>
        db.Shipments
            .Where(s => s.CustomerCode == customerCode && string.IsNullOrEmpty(s.ArrivalCountry))
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.ArrivalCountry, countryName), cancellationToken);

    /// <summary>
    /// The country's CustomerCode (which LTS_Shipments.CustomerCode is matched against to find
    /// its shipments - LTS_Integration does not carry a country id directly on the shipment) and
    /// its display name. Null when the country has no CustomerCode set, or does not exist.
    /// </summary>
    private static async Task<(string CustomerCode, string CountryName)?> CountryInfoForAsync(
        LtsIntegrationDbContext db, int countryId, CancellationToken cancellationToken)
    {
        var rawId = IntegrationCountryId.ToRawId(countryId);

        var country = await db.Countries.AsNoTracking()
            .Where(c => c.Id == rawId)
            .Select(c => new { c.CustomerCode, c.CountryDescription })
            .FirstOrDefaultAsync(cancellationToken);

        return country is null || string.IsNullOrWhiteSpace(country.CustomerCode)
            ? null
            : (country.CustomerCode, country.CountryDescription);
    }

    /// <summary>One resolved lookup per shipment attribute, keyed by the raw value stored on the shipment.</summary>
    private sealed record AttributeLookups(
        Dictionary<string, string> ArrivalCustoms,
        Dictionary<string, string> ExportType,
        Dictionary<string, string> TransportType,
        Dictionary<string, string> LoadingPoint,
        Dictionary<string, string> LogisticsCompany,
        Dictionary<string, string> Broker);

    /// <summary>
    /// Resolves the six free-text attribute columns (everything but ArrivalCountry, which comes
    /// from the real receiving country) against their LTS_-prefixed lookup tables, matching by
    /// Description - LTS_Shipments carries the attribute's Description text, not its Code. One
    /// batched query per attribute across the whole set of shipments, rather than one query per
    /// shipment per attribute.
    /// </summary>
    private static async Task<AttributeLookups> ResolveAttributesAsync(
        LtsIntegrationDbContext db, IReadOnlyList<LtsIntegrationShipment> shipments, CancellationToken cancellationToken) =>
        new(
            await ResolveOneAsync(db.ArrivalCustomsAttributes, shipments.Select(s => s.ArrivalCustoms), cancellationToken),
            await ResolveOneAsync(db.ExportTypeAttributes, shipments.Select(s => s.ExportType), cancellationToken),
            await ResolveOneAsync(db.TransportTypeAttributes, shipments.Select(s => s.TransportType), cancellationToken),
            await ResolveOneAsync(db.LoadingPointAttributes, shipments.Select(s => s.LoadingPoint), cancellationToken),
            await ResolveOneAsync(db.LogisticsCompanyAttributes, shipments.Select(s => s.LogisticsCompany), cancellationToken),
            await ResolveOneAsync(db.BrokerAttributes, shipments.Select(s => s.BrokerCompany), cancellationToken));

    private static async Task<Dictionary<string, string>> ResolveOneAsync(
        IQueryable<LtsIntegrationAttribute> table, IEnumerable<string?> rawValues, CancellationToken cancellationToken)
    {
        var values = rawValues.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Distinct().ToList();
        if (values.Count == 0)
        {
            return [];
        }

        var matches = await table.AsNoTracking()
            .Where(a => values.Contains(a.Description))
            .ToListAsync(cancellationToken);

        return matches.ToDictionary(a => a.Description, a => $"{a.Code} - {a.Description}");
    }

    /// <summary>
    /// Restricts a Broker/LogisticsCompany account to its own shipments, matched by its
    /// SupplierCompanyCode's Description (via LTS_LogisticsCompanies/LTS_Brokers) against
    /// LTS_Shipments.LogisticsCompany/BrokerCompany - those columns hold Description text, not a
    /// code, matching how the same attribute is resolved for display. Not restricted when the
    /// caller is not partner-scoped. Restricted-with-no-name means the account's company could
    /// not be resolved at all, so the caller should treat that as "matches nothing" rather than
    /// "unrestricted".
    /// </summary>
    private static async Task<(bool Restricted, string? CompanyName)> ResolvePartnerFilterAsync(
        LtsIntegrationDbContext db, UserPermissions permissions, CancellationToken cancellationToken)
    {
        if (!permissions.IsPartnerScoped)
        {
            return (false, null);
        }

        if (permissions.SupplierCompanyCode is not { } code)
        {
            return (true, null);
        }

        var table = permissions.UserType == UserType.Broker ? db.BrokerAttributes : db.LogisticsCompanyAttributes;

        var name = await table.AsNoTracking()
            .Where(a => a.Code == code)
            .Select(a => a.Description)
            .FirstOrDefaultAsync(cancellationToken);

        return (true, name);
    }

    private static IQueryable<LtsIntegrationShipment> ApplyPartnerFilter(
        IQueryable<LtsIntegrationShipment> query, UserPermissions permissions, bool restricted, string? partnerName)
    {
        if (!restricted)
        {
            return query;
        }

        return permissions.UserType == UserType.Broker
            ? query.Where(s => s.BrokerCompany == partnerName)
            : query.Where(s => s.LogisticsCompany == partnerName);
    }

    private static async Task<IQueryable<LtsIntegrationShipment>> ApplyFilterAsync(
        LtsIntegrationDbContext db, IQueryable<LtsIntegrationShipment> query, ShipmentFilter filter,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(s => s.ReferenceNo.Contains(term) || s.InvoiceNo.Contains(term));
        }

        // Filters against the stored CurrentStatus text, which only ever reaches AtCrossdock -
        // InTransitToStore/ArrivedAtStore are computed from transfers at read time (see
        // AggregateShipmentStatus) and never written back here. Filtering by either of those two
        // statuses will therefore find nothing even though rows display them - the same
        // paging-vs-computed-status tradeoff as the Transfers grid's status filter above.
        if (filter.Statuses is { Count: > 0 })
        {
            var labels = filter.Statuses.Select(v => v.ToDisplay()).ToList();
            query = query.Where(s => labels.Contains(s.CurrentStatus));
        }

        if (filter.Performances is { Count: > 0 })
        {
            var labels = filter.Performances.Select(v => v.ToDisplay()).ToList();
            query = query.Where(s => labels.Contains(s.Performance));
        }

        if (await ResolveCompanyCodeAsync(db.LogisticsCompanyAttributes, filter.LogisticsCompanyCode, cancellationToken)
            is { } logisticsCompanyName)
        {
            query = query.Where(s => s.LogisticsCompany == logisticsCompanyName);
        }

        if (await ResolveCompanyCodeAsync(db.BrokerAttributes, filter.BrokerCode, cancellationToken) is { } brokerName)
        {
            query = query.Where(s => s.BrokerCompany == brokerName);
        }

        if (filter.InvoiceDateFrom is { } from)
        {
            query = query.Where(s => s.InvoiceDate >= from);
        }

        if (filter.InvoiceDateTo is { } to)
        {
            query = query.Where(s => s.InvoiceDate <= to);
        }

        // Attribute filters (ArrivalCustomsId, ExportTypeId, ...) and OnlyInTransit are not
        // applied here: LTS_Integration's remaining five attributes are free text with no filter
        // UI wired up yet (see ShipmentFilterBar), and in-transit has no equivalent flag here.
        return query;
    }

    /// <summary>
    /// A filter dropdown's selected Code, resolved to the Description LTS_Shipments actually
    /// stores - the same match GetIntegrationAttributesAsync/ResolvePartnerFilterAsync use. Null
    /// when no code was selected or the code no longer resolves to anything.
    /// </summary>
    private static async Task<string?> ResolveCompanyCodeAsync(
        IQueryable<LtsIntegrationAttribute> table, string? code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return await table.AsNoTracking()
            .Where(a => a.Code == code)
            .Select(a => a.Description)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Groups a query's rows by key into a dictionary, tolerating duplicate keys by keeping
    /// whichever row is seen first rather than throwing. None of LTS_ShipmentDates.ReferenceNo or
    /// LTS_ShipmentTransferDates.TransferNo have a unique constraint in the hand-written DDL, so a
    /// duplicate row (bad data, a race between writers, manual SQL) is possible in practice and
    /// must degrade gracefully here rather than take the whole page down with it.
    /// </summary>
    private static async Task<Dictionary<TKey, TValue>> ToDictionaryTolerantAsync<TSource, TKey, TValue>(
        IQueryable<TSource> query, Func<TSource, TKey> keySelector, Func<TSource, TValue> valueSelector,
        CancellationToken cancellationToken) where TKey : notnull
    {
        var items = await query.ToListAsync(cancellationToken);
        return items.GroupBy(keySelector).ToDictionary(g => g.Key, g => valueSelector(g.First()));
    }

    private static IQueryable<LtsIntegrationShipment> Sort(IQueryable<LtsIntegrationShipment> query, GridRequest request)
    {
        var descending = request.SortDescending;

        return request.SortBy switch
        {
            nameof(ShipmentRow.ReferenceNo) => descending ? query.OrderByDescending(s => s.ReferenceNo) : query.OrderBy(s => s.ReferenceNo),
            nameof(ShipmentRow.InvoiceNo) => descending ? query.OrderByDescending(s => s.InvoiceNo) : query.OrderBy(s => s.InvoiceNo),
            nameof(ShipmentRow.InvoiceDate) => descending ? query.OrderByDescending(s => s.InvoiceDate) : query.OrderBy(s => s.InvoiceDate),
            nameof(ShipmentRow.CurrentStatus) => descending ? query.OrderByDescending(s => s.CurrentStatus) : query.OrderBy(s => s.CurrentStatus),
            nameof(ShipmentRow.Performance) => descending ? query.OrderByDescending(s => s.Performance) : query.OrderBy(s => s.Performance),
            nameof(ShipmentRow.LogisticsCompany) => descending ? query.OrderByDescending(s => s.LogisticsCompany) : query.OrderBy(s => s.LogisticsCompany),
            nameof(ShipmentRow.Broker) => descending ? query.OrderByDescending(s => s.BrokerCompany) : query.OrderBy(s => s.BrokerCompany),
            // LoadingDate/CrossdockArrivalDate live in the separate dates table, not sortable in
            // SQL here without a join this method has no other need for.
            _ => query.OrderByDescending(s => s.InvoiceDate).ThenByDescending(s => s.Id)
        };
    }

    private static IReadOnlyList<CountBucket<string>> AgingBuckets(IEnumerable<DateOnly?> loadingDates, DateOnly today)
    {
        var days = loadingDates.Where(d => d is not null).Select(d => today.DayNumber - d!.Value.DayNumber).ToList();

        return
        [
            new("0-3 days", "0-3 days", days.Count(d => d <= 3)),
            new("4-7 days", "4-7 days", days.Count(d => d is >= 4 and <= 7)),
            new("8-14 days", "8-14 days", days.Count(d => d is >= 8 and <= 14)),
            new("15-30 days", "15-30 days", days.Count(d => d is >= 15 and <= 30)),
            new("30+ days", "30+ days", days.Count(d => d > 30))
        ];
    }

    private static IReadOnlyList<PartnerBucket> PartnerBuckets(IEnumerable<(string? Name, PerformanceStatus Performance)> items) =>
        [
            .. items
                .GroupBy(i => i.Name ?? "Unassigned")
                .Select(g => new PartnerBucket(g.Key, g.Count(), g.Count(i => i.Performance == PerformanceStatus.Overdue)))
                .OrderByDescending(b => b.Count)
                .Take(10)
        ];

    /// <summary>
    /// A box-level date rolled up to the transfer it belongs to: reached once every box in the
    /// transfer has it, taken as the latest of them - null while any box is still missing it (or
    /// there are no boxes yet).
    /// </summary>
    private static DateOnly? BoxMilestone(IReadOnlyList<LtsIntegrationBox> boxes, Func<LtsIntegrationBox, DateOnly?> selector) =>
        boxes.Count > 0 && boxes.All(b => selector(b) is not null) ? boxes.Max(selector) : null;

    private static PerformanceStatus ParsePerformance(string value) =>
        Enum.GetValues<PerformanceStatus>().FirstOrDefault(p => p.ToDisplay() == value, PerformanceStatus.NotStarted);

    /// <summary>
    /// Neither LTS_ShipmentTransfers nor LTS_Boxes has a numeric id in the hand-written DDL;
    /// TransferRow/TransferDetail.Id is never shown or linked from, so a stable hash of the
    /// business key is enough.
    /// </summary>
    private static int SyntheticId(string transferNo) => transferNo.GetHashCode();
}

internal static class AttributeLookupExtensions
{
    /// <summary>The resolved "Code - Description" for a raw attribute value, or the raw value itself when unresolved.</summary>
    public static string? Resolve(this IReadOnlyDictionary<string, string> lookup, string? raw) =>
        raw is null ? null : lookup.GetValueOrDefault(raw, raw);
}
