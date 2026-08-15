using LTS.Domain.Entities;
using LTS.Domain.Enums;

namespace LTS.Application.Security;

/// <summary>
/// Row-level filtering. Applied in the query layer so a broker cannot reach another broker's
/// shipments through any page, export, upload or deep link — the restriction is in the SQL,
/// not in the markup.
/// </summary>
public static class ShipmentScope
{
    /// <summary>Restricts shipments to one country and, for external accounts, to their own partner.</summary>
    public static IQueryable<Shipment> Scoped(this IQueryable<Shipment> query, int countryId, UserPermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        query = query.Where(s => s.ArrivalCountryId == countryId);

        if (!permissions.IsPartnerScoped)
        {
            return query;
        }

        // An external account with no partner can see nothing rather than everything.
        var partnerId = permissions.PartnerId ?? 0;

        return permissions.UserType == UserType.Broker
            ? query.Where(s => s.BrokerId == partnerId)
            : query.Where(s => s.LogisticsCompanyId == partnerId);
    }

    /// <summary>The same restriction expressed over transfers, through their parent shipment.</summary>
    public static IQueryable<Transfer> Scoped(this IQueryable<Transfer> query, int countryId, UserPermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        query = query.Where(t => t.Shipment!.ArrivalCountryId == countryId);

        if (!permissions.IsPartnerScoped)
        {
            return query;
        }

        var partnerId = permissions.PartnerId ?? 0;

        return permissions.UserType == UserType.Broker
            ? query.Where(t => t.Shipment!.BrokerId == partnerId)
            : query.Where(t => t.Shipment!.LogisticsCompanyId == partnerId);
    }
}
