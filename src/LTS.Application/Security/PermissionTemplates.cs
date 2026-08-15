using LTS.Domain.Enums;
using LTS.Domain.Security;

namespace LTS.Application.Security;

/// <summary>A page grant proposed when an account is created.</summary>
public readonly record struct PageGrant(string PageKey, bool CanView, bool CanEdit);

/// <summary>
/// Sensible starting permissions per user type, applied when an admin creates an account and
/// then adjustable per user. Without these, onboarding a broker would mean ticking a dozen
/// boxes correctly every time.
/// </summary>
public static class PermissionTemplates
{
    public static IReadOnlyList<PageGrant> For(UserType userType) => userType switch
    {
        // Admins bypass the permission tables entirely, so they need no rows.
        UserType.Admin => [],

        UserType.LogisticsDepartment =>
        [
            new(PageKeys.Shipments, true, false),
            new(PageKeys.Transfers, true, false),
            new(PageKeys.ShipmentsOnTheWay, true, false),
            new(PageKeys.ShipmentDetails, true, true),
            new(PageKeys.DateUpload, true, true),
            new(PageKeys.AdminKpi, true, false),
            new(PageKeys.AdminAuditLog, true, false)
        ],

        // Brokers work the customs leg: they need to see the shipment and enter their two dates.
        UserType.Broker =>
        [
            new(PageKeys.Shipments, true, false),
            new(PageKeys.ShipmentsOnTheWay, true, false),
            new(PageKeys.ShipmentDetails, true, true),
            new(PageKeys.DateUpload, true, true)
        ],

        // Carriers own the journey up to the target country; the store legs are not theirs.
        UserType.LogisticsCompany =>
        [
            new(PageKeys.Shipments, true, false),
            new(PageKeys.ShipmentsOnTheWay, true, false),
            new(PageKeys.ShipmentDetails, true, true),
            new(PageKeys.DateUpload, true, true)
        ],

        _ => []
    };
}
