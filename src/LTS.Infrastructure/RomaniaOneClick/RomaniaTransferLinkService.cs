using LTS.Application.RomaniaOneClick;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.RomaniaOneClick;

/// <summary>
/// A plain field update on LTS_ShipmentTransfers - deliberately not routed through
/// IIntegrationMilestoneService, since a KLG id is an identifier, not a milestone date, and has no
/// place in LTS_MilestoneAudit.
/// </summary>
public sealed class RomaniaTransferLinkService(IDbContextFactory<LtsIntegrationDbContext> dbFactory)
    : IRomaniaTransferLinkService
{
    public async Task SetPermShipmentIdAsync(string transferNo, string? value, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var transfer = await db.ShipmentTransfers.FirstOrDefaultAsync(t => t.TransferNo == transferNo, cancellationToken);
        if (transfer is null)
        {
            return;
        }

        transfer.RomaniaPermShipmentId = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        await db.SaveChangesAsync(cancellationToken);
    }
}
