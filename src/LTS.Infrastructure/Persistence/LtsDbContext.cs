using System.Reflection;
using LTS.Application.Abstractions;
using LTS.Domain.Common;
using LTS.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Persistence;

/// <summary>
/// The LTS database. Identity tables live alongside the domain tables in one context so an
/// account and the permissions granted to it are written in a single transaction.
/// </summary>
public class LtsDbContext(DbContextOptions<LtsDbContext> options, ICurrentUser? currentUser = null, IClock? clock = null)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    private readonly IClock _clock = clock ?? new SystemClock();

    // Reference data
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<Partner> Partners => Set<Partner>();
    public DbSet<LoadingPoint> LoadingPoints => Set<LoadingPoint>();
    public DbSet<LookupValue> LookupValues => Set<LookupValue>();
    public DbSet<Store> Stores => Set<Store>();

    // Tracking
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<MilestoneAudit> MilestoneAudits => Set<MilestoneAudit>();

    // KPI
    public DbSet<KpiTarget> KpiTargets => Set<KpiTarget>();

    // Integration
    public DbSet<IntegrationSource> IntegrationSources => Set<IntegrationSource>();
    public DbSet<StatusMapping> StatusMappings => Set<StatusMapping>();
    public DbSet<IntegrationRun> IntegrationRuns => Set<IntegrationRun>();
    public DbSet<IntegrationMessage> IntegrationMessages => Set<IntegrationMessage>();

    // Security
    public DbSet<UserCountryAccess> UserCountryAccess => Set<UserCountryAccess>();
    public DbSet<UserPagePermission> UserPagePermissions => Set<UserPagePermission>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Shorter Identity table names; the default AspNetUsers/AspNetRoles reads oddly next to
        // the domain tables and this database is only ever used by LTS.
        builder.Entity<AppUser>().ToTable("Users");
        builder.Entity<IdentityRole<Guid>>().ToTable("Roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampAuditFields();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Fills in who created or changed a row. Done centrally so no service can forget it, and
    /// so background integration work is recorded as "integration" rather than as a person.
    /// </summary>
    private void StampAuditFields()
    {
        var now = _clock.UtcNow;
        var actor = currentUser?.IsAuthenticated == true
            ? currentUser.UserName ?? currentUser.UserId?.ToString()
            : "system";

        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = actor;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = actor;
                    break;
            }
        }
    }
}
