using LTS.Application.Security;
using LTS.Application.Tracking;
using LTS.Domain.Enums;
using LTS.Domain.Milestones;
using LTS.Infrastructure.Persistence;
using LTS.Infrastructure.Reference;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Tracking;

/// <summary>
/// Reads LTS_MilestoneAudit, scoped by country (via the shipment's CustomerCode, same as
/// everywhere else in this app) and, for a partner-scoped account, restricted to its own
/// shipments the same way IntegrationShipmentQueryService restricts the Shipments/Transfers grids -
/// so the log can't leak a shipment the reader couldn't otherwise see.
/// </summary>
public sealed class IntegrationAuditQueryService(
    IDbContextFactory<LtsIntegrationDbContext> dbFactory) : IIntegrationAuditQueryService
{
    public async Task<PagedResult<AuditRow>> GetAuditAsync(
        int countryId, UserPermissions permissions, AuditFilter filter, GridRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(request);

        if (!permissions.HasCountry(countryId))
        {
            return PagedResult<AuditRow>.Empty;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var rawCountryId = IntegrationCountryId.ToRawId(countryId);

        var customerCode = await db.Countries.AsNoTracking()
            .Where(c => c.Id == rawCountryId)
            .Select(c => c.CustomerCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(customerCode))
        {
            return PagedResult<AuditRow>.Empty;
        }

        var (restricted, partnerName) = await ResolvePartnerFilterAsync(db, permissions, cancellationToken);
        if (restricted && partnerName is null)
        {
            return PagedResult<AuditRow>.Empty;
        }

        var query =
            from a in db.MilestoneAudits.AsNoTracking()
            join s in db.Shipments.AsNoTracking() on a.ReferenceNo equals s.ReferenceNo
            where s.CustomerCode == customerCode
            select new { Audit = a, s.BrokerCompany, s.LogisticsCompany };

        if (partnerName is not null)
        {
            query = permissions.UserType == UserType.Broker
                ? query.Where(x => x.BrokerCompany == partnerName)
                : query.Where(x => x.LogisticsCompany == partnerName);
        }

        if (filter.Source is { } source)
        {
            query = query.Where(x => x.Audit.Source == source);
        }

        if (filter.MilestoneType is { } type)
        {
            query = query.Where(x => x.Audit.MilestoneType == type);
        }

        if (filter.From is { } from)
        {
            query = query.Where(x => x.Audit.ChangedAt >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(x => x.Audit.ChangedAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(x =>
                x.Audit.ReferenceNo.Contains(term) || (x.Audit.TransferNo != null && x.Audit.TransferNo.Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);

        var page = await query
            .OrderByDescending(x => x.Audit.ChangedAt)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(x => x.Audit)
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditRow>([.. page.Select(Map)], total);
    }

    /// <summary>
    /// Mirrors IntegrationShipmentQueryService.ResolvePartnerFilterAsync: restricted-with-no-name
    /// means the account's company could not be resolved, so the caller should treat that as
    /// "matches nothing" rather than "unrestricted".
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

    private static AuditRow Map(LtsIntegrationMilestoneAudit audit) => new()
    {
        Id = audit.Id,
        ReferenceNo = audit.ReferenceNo,
        TransferNo = audit.TransferNo,
        MilestoneType = audit.MilestoneType,
        MilestoneName = MilestoneCatalog.DisplayName(audit.MilestoneType),
        OldValue = audit.OldValue,
        NewValue = audit.NewValue,
        Source = audit.Source,
        UserName = audit.UserName,
        ChangedAt = audit.ChangedAt,
        Note = audit.Note
    };
}
