using LTS.Application.Abstractions;
using LTS.Application.Security;
using LTS.Application.Tracking;
using LTS.Domain.Entities;
using LTS.Domain.Enums;
using LTS.Domain.Milestones;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Tracking;

/// <summary>
/// Reads for the tracking pages. Scoping is applied first and always, projections are done in
/// SQL, and paging is server-side — the grids are expected to sit over hundreds of thousands
/// of rows.
/// </summary>
public sealed class ShipmentQueryService(LtsDbContext db, IClock clock) : IShipmentQueryService
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

        var query = Filtered(db.Shipments.AsNoTracking(), countryId, permissions, filter);

        var total = await query.CountAsync(cancellationToken);

        var rows = await Sort(query, request)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(s => new ShipmentRow
            {
                Id = s.Id,
                ReferenceNo = s.ReferenceNo,
                InvoiceNo = s.InvoiceNo,
                InvoiceDate = s.InvoiceDate,
                ArrivalCountry = s.ArrivalCountry!.Name,
                ArrivalCustoms = s.ArrivalCustoms!.Name,
                ExportType = s.ExportType!.Name,
                TransportType = s.TransportType!.Name,
                LoadingPoint = s.LoadingPoint!.Name,
                LoadingCountryCode = s.LoadingPoint!.CountryCode,
                LogisticsCompany = s.LogisticsCompany!.Name,
                Broker = s.Broker!.Name,
                LoadingDate = s.LoadingDate,
                DepartureCustomsClearanceDate = s.DepartureCustomsClearanceDate,
                DepartureDate = s.DepartureDate,
                ArrivalToTargetCountryDate = s.ArrivalToTargetCountryDate,
                CustomsStartDate = s.CustomsStartDate,
                CustomsEndDate = s.CustomsEndDate,
                CrossdockArrivalDate = s.CrossdockArrivalDate,
                TransferCount = s.TransferCount,
                TotalBoxes = s.TotalBoxes,
                TotalItems = s.TotalItems,
                CurrentStatus = s.CurrentStatus,
                CurrentStatusDate = s.CurrentStatusDate,
                Performance = s.Performance
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<ShipmentRow>(rows, total);
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

        var query = db.Transfers.AsNoTracking().Scoped(countryId, permissions);
        query = ApplyTransferFilter(query, filter);

        var total = await query.CountAsync(cancellationToken);

        var rows = await SortTransfers(query, request)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(t => new TransferRow
            {
                Id = t.Id,
                ShipmentId = t.ShipmentId,
                TransferNo = t.TransferNo,
                ReferenceNo = t.Shipment!.ReferenceNo,
                InvoiceNo = t.Shipment!.InvoiceNo,
                DateCreated = t.Shipment!.InvoiceDate,
                StoreCode = t.Store!.Code,
                StoreName = t.Store!.Name,
                CurrentStatus = t.CurrentStatus,
                Performance = t.Performance,
                TotalBoxes = t.TotalBoxes,
                TotalItems = t.TotalItems,
                CrossdockDepartureDate = t.CrossdockDepartureDate,
                PlannedStoreArrivalDate = t.PlannedStoreArrivalDate,
                StoreArrivalDate = t.StoreArrivalDate,
                StorePreAcceptanceDate = t.StorePreAcceptanceDate,
                StoreAcceptanceDate = t.StoreAcceptanceDate
            })
            .ToListAsync(cancellationToken);

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

        var shipment = await db.Shipments
            .AsNoTracking()
            .Scoped(countryId, permissions)
            .Include(s => s.ArrivalCountry)
            .Include(s => s.ArrivalCustoms)
            .Include(s => s.ExportType)
            .Include(s => s.TransportType)
            .Include(s => s.LoadingPoint)
            .Include(s => s.LogisticsCompany)
            .Include(s => s.Broker)
            .Include(s => s.Transfers).ThenInclude(t => t.Store)
            .FirstOrDefaultAsync(s => s.ReferenceNo == reference || s.InvoiceNo == reference, cancellationToken);

        if (shipment is null)
        {
            return null;
        }

        return new ShipmentDetail
        {
            Id = shipment.Id,
            ArrivalCountryId = shipment.ArrivalCountryId,
            ReferenceNo = shipment.ReferenceNo,
            InvoiceNo = shipment.InvoiceNo,
            InvoiceDate = shipment.InvoiceDate,
            ArrivalCountry = shipment.ArrivalCountry?.Name,
            ArrivalCustoms = shipment.ArrivalCustoms?.Name,
            ExportType = shipment.ExportType?.Name,
            TransportType = shipment.TransportType?.Name,
            LoadingPoint = shipment.LoadingPoint?.Name,
            LogisticsCompany = shipment.LogisticsCompany?.Name,
            Broker = shipment.Broker?.Name,
            CurrentStatus = shipment.CurrentStatus,
            Performance = shipment.Performance,
            TransferCount = shipment.TransferCount,
            TotalBoxes = shipment.TotalBoxes,
            TotalItems = shipment.TotalItems,
            Milestones = shipment.GetMilestoneDates(),
            Transfers =
            [
                .. shipment.Transfers
                    .OrderBy(t => t.TransferNo)
                    .Select(t => new TransferDetail
                    {
                        Id = t.Id,
                        TransferNo = t.TransferNo,
                        Receiver = t.Store is null ? string.Empty : t.Store.DisplayName,
                        TotalBoxes = t.TotalBoxes,
                        TotalItems = t.TotalItems,
                        CurrentStatus = t.CurrentStatus,
                        Performance = t.Performance,
                        Milestones = MilestoneCatalog.TransferMilestones
                            .ToDictionary(d => d.Type, d => t.GetMilestoneDate(d.Type))
                    })
            ]
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

        // In transit means the shipment has not finished delivering: at least one of its
        // transfers has no store arrival, or it has not been split yet.
        var query = db.Shipments
            .AsNoTracking()
            .Scoped(countryId, permissions)
            .Where(s => s.Transfers.Count == 0 || s.Transfers.Any(t => t.StoreArrivalDate == null));

        var totals = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Shipments = g.Count(),
                Transfers = g.Sum(s => s.TransferCount),
                Boxes = g.Sum(s => s.TotalBoxes),
                Items = g.Sum(s => s.TotalItems),
                Overdue = g.Count(s => s.Performance == PerformanceStatus.Overdue),
                AtRisk = g.Count(s => s.Performance == PerformanceStatus.AtRisk)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (totals is null)
        {
            return InTransitSummary.Empty;
        }

        var byStatus = await query
            .GroupBy(s => s.CurrentStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var byPerformance = await query
            .GroupBy(s => s.Performance)
            .Select(g => new { Performance = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var aging = await AgingBucketsAsync(query, cancellationToken);

        var byLogistics = await PartnerBucketsAsync(query, s => s.LogisticsCompany!.Name, cancellationToken);
        var byBroker = await PartnerBucketsAsync(query, s => s.Broker!.Name, cancellationToken);

        return new InTransitSummary
        {
            ShipmentCount = totals.Shipments,
            TransferCount = totals.Transfers,
            TotalBoxes = totals.Boxes,
            TotalItems = totals.Items,
            OverdueCount = totals.Overdue,
            AtRiskCount = totals.AtRisk,
            ByStatus =
            [
                .. byStatus
                    .OrderBy(x => x.Status)
                    .Select(x => new CountBucket<TrackingStatus>(x.Status, x.Status.ToDisplay(), x.Count))
            ],
            ByPerformance =
            [
                .. byPerformance
                    .OrderBy(x => x.Performance)
                    .Select(x => new CountBucket<PerformanceStatus>(x.Performance, x.Performance.ToDisplay(), x.Count))
            ],
            Aging = aging,
            ByLogisticsCompany = byLogistics,
            ByBroker = byBroker
        };
    }

    private static async Task<IReadOnlyList<PartnerBucket>> PartnerBucketsAsync(
        IQueryable<Shipment> query,
        System.Linq.Expressions.Expression<Func<Shipment, string>> nameSelector,
        CancellationToken cancellationToken)
    {
        var rows = await query
            .GroupBy(nameSelector)
            .Select(g => new
            {
                Name = g.Key,
                Count = g.Count(),
                Overdue = g.Count(s => s.Performance == PerformanceStatus.Overdue)
            })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => new PartnerBucket(r.Name ?? "Unassigned", r.Count, r.Overdue))];
    }

    /// <summary>
    /// Days-since-loading buckets, chosen to separate "normal" from "someone should call".
    /// Counted in SQL as conditional aggregates rather than by pulling every loading date back.
    /// </summary>
    private async Task<IReadOnlyList<CountBucket<string>>> AgingBucketsAsync(
        IQueryable<Shipment> query, CancellationToken cancellationToken)
    {
        var today = clock.Today;

        var counts = await query
            .Where(s => s.LoadingDate != null)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                UpTo3 = g.Count(s => EF.Functions.DateDiffDay(s.LoadingDate!.Value, today) <= 3),
                UpTo7 = g.Count(s => EF.Functions.DateDiffDay(s.LoadingDate!.Value, today) >= 4
                                     && EF.Functions.DateDiffDay(s.LoadingDate!.Value, today) <= 7),
                UpTo14 = g.Count(s => EF.Functions.DateDiffDay(s.LoadingDate!.Value, today) >= 8
                                      && EF.Functions.DateDiffDay(s.LoadingDate!.Value, today) <= 14),
                UpTo30 = g.Count(s => EF.Functions.DateDiffDay(s.LoadingDate!.Value, today) >= 15
                                      && EF.Functions.DateDiffDay(s.LoadingDate!.Value, today) <= 30),
                Over30 = g.Count(s => EF.Functions.DateDiffDay(s.LoadingDate!.Value, today) > 30)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return
        [
            new("0-3 days", "0-3 days", counts?.UpTo3 ?? 0),
            new("4-7 days", "4-7 days", counts?.UpTo7 ?? 0),
            new("8-14 days", "8-14 days", counts?.UpTo14 ?? 0),
            new("15-30 days", "15-30 days", counts?.UpTo30 ?? 0),
            new("30+ days", "30+ days", counts?.Over30 ?? 0)
        ];
    }

    private static IQueryable<Shipment> Filtered(
        IQueryable<Shipment> query, int countryId, UserPermissions permissions, ShipmentFilter filter)
    {
        query = query.Scoped(countryId, permissions);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(s =>
                s.ReferenceNo.Contains(term) ||
                s.InvoiceNo.Contains(term) ||
                s.Transfers.Any(t => t.TransferNo.Contains(term)));
        }

        if (filter.Statuses is { Count: > 0 })
        {
            query = query.Where(s => filter.Statuses.Contains(s.CurrentStatus));
        }

        if (filter.Performances is { Count: > 0 })
        {
            query = query.Where(s => filter.Performances.Contains(s.Performance));
        }

        if (filter.ArrivalCustomsId is { } customsId)
        {
            query = query.Where(s => s.ArrivalCustomsId == customsId);
        }

        if (filter.ExportTypeId is { } exportTypeId)
        {
            query = query.Where(s => s.ExportTypeId == exportTypeId);
        }

        if (filter.TransportTypeId is { } transportTypeId)
        {
            query = query.Where(s => s.TransportTypeId == transportTypeId);
        }

        if (filter.LoadingPointId is { } loadingPointId)
        {
            query = query.Where(s => s.LoadingPointId == loadingPointId);
        }

        if (filter.LogisticsCompanyId is { } logisticsCompanyId)
        {
            query = query.Where(s => s.LogisticsCompanyId == logisticsCompanyId);
        }

        if (filter.BrokerId is { } brokerId)
        {
            query = query.Where(s => s.BrokerId == brokerId);
        }

        if (filter.InvoiceDateFrom is { } from)
        {
            query = query.Where(s => s.InvoiceDate >= from);
        }

        if (filter.InvoiceDateTo is { } to)
        {
            query = query.Where(s => s.InvoiceDate <= to);
        }

        if (filter.OnlyInTransit)
        {
            query = query.Where(s => s.Transfers.Count == 0 || s.Transfers.Any(t => t.StoreArrivalDate == null));
        }

        return query;
    }

    private static IQueryable<Transfer> ApplyTransferFilter(IQueryable<Transfer> query, ShipmentFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(t =>
                t.TransferNo.Contains(term) ||
                t.Shipment!.ReferenceNo.Contains(term) ||
                t.Shipment!.InvoiceNo.Contains(term) ||
                t.Store!.Code.Contains(term) ||
                t.Store!.Name.Contains(term));
        }

        if (filter.Statuses is { Count: > 0 })
        {
            query = query.Where(t => filter.Statuses.Contains(t.CurrentStatus));
        }

        if (filter.Performances is { Count: > 0 })
        {
            query = query.Where(t => filter.Performances.Contains(t.Performance));
        }

        if (filter.LogisticsCompanyId is { } logisticsCompanyId)
        {
            query = query.Where(t => t.Shipment!.LogisticsCompanyId == logisticsCompanyId);
        }

        if (filter.BrokerId is { } brokerId)
        {
            query = query.Where(t => t.Shipment!.BrokerId == brokerId);
        }

        if (filter.InvoiceDateFrom is { } from)
        {
            query = query.Where(t => t.Shipment!.InvoiceDate >= from);
        }

        if (filter.InvoiceDateTo is { } to)
        {
            query = query.Where(t => t.Shipment!.InvoiceDate <= to);
        }

        if (filter.OnlyInTransit)
        {
            query = query.Where(t => t.StoreArrivalDate == null);
        }

        return query;
    }

    private static IQueryable<Shipment> Sort(IQueryable<Shipment> query, GridRequest request)
    {
        var descending = request.SortDescending;

        return request.SortBy switch
        {
            nameof(ShipmentRow.ReferenceNo) => query.OrderByDirection(s => s.ReferenceNo, descending),
            nameof(ShipmentRow.InvoiceNo) => query.OrderByDirection(s => s.InvoiceNo, descending),
            nameof(ShipmentRow.InvoiceDate) => query.OrderByDirection(s => s.InvoiceDate, descending),
            nameof(ShipmentRow.CurrentStatus) => query.OrderByDirection(s => s.CurrentStatus, descending),
            nameof(ShipmentRow.Performance) => query.OrderByDirection(s => s.Performance, descending),
            nameof(ShipmentRow.LoadingDate) => query.OrderByDirection(s => s.LoadingDate, descending),
            nameof(ShipmentRow.CrossdockArrivalDate) => query.OrderByDirection(s => s.CrossdockArrivalDate, descending),
            nameof(ShipmentRow.LogisticsCompany) => query.OrderByDirection(s => s.LogisticsCompany!.Name, descending),
            nameof(ShipmentRow.Broker) => query.OrderByDirection(s => s.Broker!.Name, descending),
            // Newest first is what a tracking desk wants to see when it opens the page.
            _ => query.OrderByDescending(s => s.InvoiceDate).ThenByDescending(s => s.Id)
        };
    }

    private static IQueryable<Transfer> SortTransfers(IQueryable<Transfer> query, GridRequest request)
    {
        var descending = request.SortDescending;

        return request.SortBy switch
        {
            nameof(TransferRow.TransferNo) => query.OrderByDirection(t => t.TransferNo, descending),
            nameof(TransferRow.InvoiceNo) => query.OrderByDirection(t => t.Shipment!.InvoiceNo, descending),
            nameof(TransferRow.DateCreated) => query.OrderByDirection(t => t.Shipment!.InvoiceDate, descending),
            nameof(TransferRow.Receiver) => query.OrderByDirection(t => t.Store!.Code, descending),
            nameof(TransferRow.CurrentStatus) => query.OrderByDirection(t => t.CurrentStatus, descending),
            nameof(TransferRow.Performance) => query.OrderByDirection(t => t.Performance, descending),
            nameof(TransferRow.StoreArrivalDate) => query.OrderByDirection(t => t.StoreArrivalDate, descending),
            _ => query.OrderByDescending(t => t.Shipment!.InvoiceDate).ThenBy(t => t.TransferNo)
        };
    }

}

internal static class QueryableSortExtensions
{
    /// <summary>Orders ascending or descending from a runtime flag, so each sort needs one line.</summary>
    public static IOrderedQueryable<T> OrderByDirection<T, TKey>(
        this IQueryable<T> query,
        System.Linq.Expressions.Expression<Func<T, TKey>> selector,
        bool descending) =>
        descending ? query.OrderByDescending(selector) : query.OrderBy(selector);
}
