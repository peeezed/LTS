using LTS.Application.Security;
using LTS.Application.Tracking;
using LTS.Domain.Enums;
using LTS.Domain.Milestones;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Tracking;

public sealed class AuditQueryService(LtsDbContext db) : IAuditQueryService
{
    public async Task<PagedResult<AuditRow>> GetAuditAsync(
        int countryId,
        UserPermissions permissions,
        AuditFilter filter,
        GridRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (!permissions.HasCountry(countryId))
        {
            return PagedResult<AuditRow>.Empty;
        }

        // Audit rows are reached through their shipment, which is what applies the country and
        // partner scope — the log cannot leak a shipment the reader could not otherwise see.
        var visibleShipments = db.Shipments.AsNoTracking().Scoped(countryId, permissions).Select(s => s.Id);

        var query = db.MilestoneAudits
            .AsNoTracking()
            .Where(a => visibleShipments.Contains(a.ShipmentId));

        if (filter.Source is { } source)
        {
            query = query.Where(a => a.Source == source);
        }

        if (filter.MilestoneType is { } milestoneType)
        {
            query = query.Where(a => a.MilestoneType == milestoneType);
        }

        if (filter.From is { } from)
        {
            query = query.Where(a => a.ChangedAt >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(a => a.ChangedAt <= to);
        }

        var joined = query
            .Join(db.Shipments.AsNoTracking(), a => a.ShipmentId, s => s.Id, (a, s) => new { Audit = a, s.ReferenceNo })
            .Select(x => new
            {
                x.Audit,
                x.ReferenceNo,
                TransferNo = x.Audit.Scope == MilestoneScope.Transfer
                    ? db.Transfers.Where(t => t.Id == x.Audit.EntityId).Select(t => t.TransferNo).FirstOrDefault()
                    : null
            });

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            joined = joined.Where(x => x.ReferenceNo.Contains(term) ||
                                       (x.TransferNo != null && x.TransferNo.Contains(term)));
        }

        var total = await joined.CountAsync(cancellationToken);

        var rows = await joined
            .OrderByDescending(x => x.Audit.ChangedAt)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditRow>(
        [
            .. rows.Select(x => new AuditRow
            {
                Id = x.Audit.Id,
                ReferenceNo = x.ReferenceNo,
                TransferNo = x.TransferNo,
                MilestoneType = x.Audit.MilestoneType,
                MilestoneName = MilestoneCatalog.DisplayName(x.Audit.MilestoneType),
                OldValue = x.Audit.OldValue,
                NewValue = x.Audit.NewValue,
                Source = x.Audit.Source,
                UserName = x.Audit.UserName,
                ChangedAt = x.Audit.ChangedAt,
                Note = x.Audit.Note
            })
        ], total);
    }
}
