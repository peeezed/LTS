using LTS.Application.Abstractions;
using LTS.Application.Security;
using LTS.Application.Tracking;
using LTS.Domain.Enums;
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
        if (!permissions.HasCountry(countryId) || permissions.IsPartnerScoped)
        {
            return PagedResult<ShipmentRow>.Empty;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var customerCode = await CustomerCodeForAsync(db, countryId, cancellationToken);
        if (customerCode is null)
        {
            return PagedResult<ShipmentRow>.Empty;
        }

        var query = ApplyFilter(db.Shipments.AsNoTracking().Where(s => s.CustomerCode == customerCode), filter);

        var total = await query.CountAsync(cancellationToken);

        var page = await Sort(query, request)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var references = page.Select(s => s.ReferenceNo).ToList();
        var dates = await db.ShipmentDates.AsNoTracking()
            .Where(d => references.Contains(d.ReferenceNo))
            .ToDictionaryAsync(d => d.ReferenceNo, cancellationToken);

        var rows = page.Select(s =>
        {
            var d = dates.GetValueOrDefault(s.ReferenceNo);

            return new ShipmentRow
            {
                Id = s.Id,
                ReferenceNo = s.ReferenceNo,
                InvoiceNo = s.InvoiceNo,
                InvoiceDate = s.InvoiceDate,
                ArrivalCountry = s.ArrivalCountry,
                ArrivalCustoms = s.ArrivalCustoms,
                ExportType = s.ExportType,
                TransportType = s.TransportType,
                LoadingPoint = s.LoadingPoint,
                LoadingCountryCode = null,
                LogisticsCompany = s.LogisticsCompany,
                Broker = s.BrokerCompany,
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
                CurrentStatus = ParseStatus(s.CurrentStatus),
                CurrentStatusDate = null,
                Performance = ParsePerformance(s.Performance)
            };
        }).ToList();

        return new PagedResult<ShipmentRow>(rows, total);
    }

    public async Task<PagedResult<TransferRow>> GetTransfersAsync(
        int countryId,
        UserPermissions permissions,
        ShipmentFilter filter,
        GridRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!permissions.HasCountry(countryId) || permissions.IsPartnerScoped)
        {
            return PagedResult<TransferRow>.Empty;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var customerCode = await CustomerCodeForAsync(db, countryId, cancellationToken);
        if (customerCode is null)
        {
            return PagedResult<TransferRow>.Empty;
        }

        var query =
            from t in db.ShipmentTransfers.AsNoTracking()
            join s in db.Shipments.AsNoTracking() on t.ReferenceNo equals s.ReferenceNo
            where s.CustomerCode == customerCode
            select new { Transfer = t, Shipment = s };

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(x =>
                x.Transfer.TransferNo.Contains(term) ||
                x.Shipment.ReferenceNo.Contains(term) ||
                x.Shipment.InvoiceNo.Contains(term));
        }

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
        var dates = await db.ShipmentTransferDates.AsNoTracking()
            .Where(d => transferNos.Contains(d.TransferNo))
            .ToDictionaryAsync(d => d.TransferNo, cancellationToken);
        var boxes = await db.Boxes.AsNoTracking()
            .Where(b => transferNos.Contains(b.TransferNo))
            .ToListAsync(cancellationToken);

        var rows = page.Select(x =>
        {
            var d = dates.GetValueOrDefault(x.Transfer.TransferNo);
            var transferBoxes = boxes.Where(b => b.TransferNo == x.Transfer.TransferNo).ToList();

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
                CurrentStatus = ParseStatus(x.Transfer.CurrentStatus),
                Performance = ParsePerformance(x.Transfer.Performance),
                TotalBoxes = x.Transfer.TotalBoxes ?? 0,
                TotalItems = x.Transfer.TotalItems ?? 0,
                CrossdockDepartureDate = d?.CrossdockDepartureDate,
                PlannedStoreArrivalDate = d?.PlannedStoreArrivalDate,
                StoreArrivalDate = d?.StoreArrivalDate,
                StorePreAcceptanceDate = BoxMilestone(transferBoxes, b => b.PreAcceptanceDate),
                StoreAcceptanceDate = BoxMilestone(transferBoxes, b => b.AcceptanceDate)
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
        if (!permissions.HasCountry(countryId) || permissions.IsPartnerScoped || string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        reference = reference.Trim();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var customerCode = await CustomerCodeForAsync(db, countryId, cancellationToken);
        if (customerCode is null)
        {
            return null;
        }

        var shipment = await db.Shipments.AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.CustomerCode == customerCode && (s.ReferenceNo == reference || s.InvoiceNo == reference),
                cancellationToken);

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
        var transferDates = await db.ShipmentTransferDates.AsNoTracking()
            .Where(d => transferNos.Contains(d.TransferNo))
            .ToDictionaryAsync(d => d.TransferNo, cancellationToken);
        var boxes = await db.Boxes.AsNoTracking()
            .Where(b => transferNos.Contains(b.TransferNo))
            .ToListAsync(cancellationToken);

        return new ShipmentDetail
        {
            Id = shipment.Id,
            ArrivalCountryId = countryId,
            ReferenceNo = shipment.ReferenceNo,
            InvoiceNo = shipment.InvoiceNo,
            InvoiceDate = shipment.InvoiceDate,
            ArrivalCountry = shipment.ArrivalCountry,
            ArrivalCustoms = shipment.ArrivalCustoms,
            ExportType = shipment.ExportType,
            TransportType = shipment.TransportType,
            LoadingPoint = shipment.LoadingPoint,
            LogisticsCompany = shipment.LogisticsCompany,
            Broker = shipment.BrokerCompany,
            CurrentStatus = ParseStatus(shipment.CurrentStatus),
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
            Transfers =
            [
                .. transfers.Select(t =>
                {
                    var td = transferDates.GetValueOrDefault(t.TransferNo);
                    var transferBoxes = boxes.Where(b => b.TransferNo == t.TransferNo).ToList();

                    return new TransferDetail
                    {
                        Id = SyntheticId(t.TransferNo),
                        TransferNo = t.TransferNo,
                        Receiver = t.ReceivingStoreCode ?? string.Empty,
                        TotalBoxes = t.TotalBoxes ?? 0,
                        TotalItems = t.TotalItems ?? 0,
                        CurrentStatus = ParseStatus(t.CurrentStatus),
                        Performance = ParsePerformance(t.Performance),
                        Milestones = new Dictionary<MilestoneType, DateOnly?>
                        {
                            [MilestoneType.CrossdockDeparture] = td?.CrossdockDepartureDate,
                            [MilestoneType.PlannedStoreArrival] = td?.PlannedStoreArrivalDate,
                            [MilestoneType.StoreArrival] = td?.StoreArrivalDate,
                            [MilestoneType.StorePreAcceptance] = BoxMilestone(transferBoxes, b => b.PreAcceptanceDate),
                            [MilestoneType.StoreAcceptance] = BoxMilestone(transferBoxes, b => b.AcceptanceDate)
                        }
                    };
                })
            ]
        };
    }

    public async Task<InTransitSummary> GetInTransitSummaryAsync(
        int countryId,
        UserPermissions permissions,
        CancellationToken cancellationToken = default)
    {
        if (!permissions.HasCountry(countryId) || permissions.IsPartnerScoped)
        {
            return InTransitSummary.Empty;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var customerCode = await CustomerCodeForAsync(db, countryId, cancellationToken);
        if (customerCode is null)
        {
            return InTransitSummary.Empty;
        }

        // "In transit" is approximated from the shipment's own CurrentStatus, since
        // LTS_Integration does not (yet) carry enough transfer-level detail here to check every
        // store leg the way the old database's in-transit query does.
        var accepted = TrackingStatus.Accepted.ToDisplay();
        var shipments = await db.Shipments.AsNoTracking()
            .Where(s => s.CustomerCode == customerCode && s.CurrentStatus != accepted)
            .ToListAsync(cancellationToken);

        if (shipments.Count == 0)
        {
            return InTransitSummary.Empty;
        }

        var references = shipments.Select(s => s.ReferenceNo).ToList();
        var loadingDates = await db.ShipmentDates.AsNoTracking()
            .Where(d => references.Contains(d.ReferenceNo))
            .ToDictionaryAsync(d => d.ReferenceNo, d => d.LoadingDate, cancellationToken);

        var items = shipments
            .Select(s => new
            {
                Shipment = s,
                Status = ParseStatus(s.CurrentStatus),
                Performance = ParsePerformance(s.Performance),
                LoadingDate = loadingDates.GetValueOrDefault(s.ReferenceNo)
            })
            .ToList();

        var today = clock.Today;

        return new InTransitSummary
        {
            ShipmentCount = items.Count,
            TransferCount = shipments.Sum(s => s.TotalTransfers ?? 0),
            TotalBoxes = shipments.Sum(s => s.TotalBoxes ?? 0),
            TotalItems = shipments.Sum(s => s.TotalItems ?? 0),
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
    /// The country's CustomerCode, which LTS_Shipments.CustomerCode is matched against to find
    /// its shipments - LTS_Integration does not carry a country id directly on the shipment.
    /// Null when the country has no CustomerCode set, or does not exist.
    /// </summary>
    private static async Task<string?> CustomerCodeForAsync(
        LtsIntegrationDbContext db, int countryId, CancellationToken cancellationToken)
    {
        var rawId = IntegrationCountryId.ToRawId(countryId);

        var customerCode = await db.Countries.AsNoTracking()
            .Where(c => c.Id == rawId)
            .Select(c => c.CustomerCode)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(customerCode) ? null : customerCode;
    }

    private static IQueryable<LtsIntegrationShipment> ApplyFilter(
        IQueryable<LtsIntegrationShipment> query, ShipmentFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(s => s.ReferenceNo.Contains(term) || s.InvoiceNo.Contains(term));
        }

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

        if (filter.InvoiceDateFrom is { } from)
        {
            query = query.Where(s => s.InvoiceDate >= from);
        }

        if (filter.InvoiceDateTo is { } to)
        {
            query = query.Where(s => s.InvoiceDate <= to);
        }

        // Attribute filters (ArrivalCustomsId, ExportTypeId, ...) and OnlyInTransit are not
        // applied here: LTS_Integration's seven attributes are free text, not the old lookup
        // ids the filter dropdowns are built from, and their dropdowns have nothing to show for
        // an LTS_Integration-backed country anyway (see ShipmentFilterBar).
        return query;
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

    private static TrackingStatus ParseStatus(string value) =>
        Enum.GetValues<TrackingStatus>().FirstOrDefault(s => s.ToDisplay() == value, TrackingStatus.Created);

    private static PerformanceStatus ParsePerformance(string value) =>
        Enum.GetValues<PerformanceStatus>().FirstOrDefault(p => p.ToDisplay() == value, PerformanceStatus.NotStarted);

    /// <summary>
    /// Neither LTS_ShipmentTransfers nor LTS_Boxes has a numeric id in the hand-written DDL;
    /// TransferRow/TransferDetail.Id is never shown or linked from, so a stable hash of the
    /// business key is enough.
    /// </summary>
    private static int SyntheticId(string transferNo) => transferNo.GetHashCode();
}
