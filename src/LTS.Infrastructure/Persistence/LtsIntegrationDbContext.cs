using LTS.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Persistence;

/// <summary>
/// Access to LTS_Integration, the external database the app is being migrated onto one table at
/// a time. Its schema is managed by hand, not by EF migrations, so this context only ever maps
/// tables that already exist - it never creates or migrates anything.
///
/// This is also, as of the authentication cutover, the database accounts sign in against:
/// Identity's UserStore touches Roles/Claims/Logins/Tokens on every sign-in even though this app
/// populates none of them (see AppUserClaimsPrincipalFactory), so all of them are mapped here too
/// - LTS_-prefixed twins of the old database's Identity tables.
/// </summary>
public class LtsIntegrationDbContext(DbContextOptions<LtsIntegrationDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<LtsIntegrationCountry> Countries => Set<LtsIntegrationCountry>();

    public DbSet<UserCountryAccess> UserCountryAccess => Set<UserCountryAccess>();
    public DbSet<UserPagePermission> UserPagePermissions => Set<UserPagePermission>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<LtsIntegrationCountry>(entity =>
        {
            entity.ToTable("LTS_Countries");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).HasColumnName("ID");
            entity.Property(c => c.CountryCode).HasColumnName("CountryCode").HasMaxLength(2).IsRequired();
            entity.Property(c => c.CountryDescription).HasColumnName("CountryDescription").HasMaxLength(50).IsRequired();
            entity.Property(c => c.CustomerCode).HasColumnName("CustomerCode").HasMaxLength(50);
            entity.Property(c => c.IsActive).HasColumnName("IsActive");
        });

        // AppUser's Partner and UserCountryAccess/UserPagePermission's Country navigations point
        // at entities this context does not (yet) map - LTS_Integration has no Partners table,
        // and LTS_Countries is a different shape than LTS.Domain.Entities.Country. The ids
        // (PartnerId, CountryId) are still plain columns; only the navigation is unavailable here.
        // CountryAccess/PagePermissions are ignored too: callers query the DbSets directly
        // (db.UserCountryAccess.Where(a => a.UserId == id)), never through the AppUser
        // navigation, so this avoids EF discovering two different relationships to the same
        // tables (the explicit HasOne below, and an auto-discovered one from these collections).
        builder.Entity<AppUser>(entity =>
        {
            entity.ToTable("LTS_Users");
            entity.Property(u => u.Id).HasColumnName("ID");
            // LTS_Users.UserType is nvarchar (e.g. "Admin"), matching this database's convention
            // of storing enums as readable text - EF's default is int, so this must be explicit.
            entity.Property(u => u.UserType).HasConversion<string>().HasMaxLength(50);
            entity.Ignore(u => u.Partner);
            entity.Ignore(u => u.CountryAccess);
            entity.Ignore(u => u.PagePermissions);
        });

        builder.Entity<IdentityRole<Guid>>(entity =>
        {
            entity.ToTable("LTS_Roles");
            entity.Property(r => r.Id).HasColumnName("ID");
        });

        builder.Entity<IdentityUserRole<Guid>>().ToTable("LTS_UserRoles");

        builder.Entity<IdentityUserClaim<Guid>>(entity =>
        {
            entity.ToTable("LTS_UserClaims");
            entity.Property(c => c.Id).HasColumnName("ID");
        });

        builder.Entity<IdentityUserLogin<Guid>>().ToTable("LTS_UserLogins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("LTS_UserTokens");

        builder.Entity<IdentityRoleClaim<Guid>>(entity =>
        {
            entity.ToTable("LTS_RoleClaims");
            entity.Property(c => c.Id).HasColumnName("ID");
        });

        // CountryId/PartnerId here are raw LTS_Integration ids (real FK to LTS_Countries at the
        // SQL level) - callers convert to/from the app-wide offset id at the service boundary,
        // via IntegrationCountryId. UserId is a plain scalar column, not an EF-level
        // relationship: the real foreign key to LTS_Users (with cascade delete) already exists
        // in the hand-written DDL, and neither entity has (or needs) a navigation property back
        // to AppUser, since every query here goes through the DbSet directly. Declaring an
        // EF-level HasOne/WithMany on top of that made the model builder discover a second,
        // conflicting relationship and fall back to a shadow 'UserId1' column.
        builder.Entity<UserCountryAccess>(entity =>
        {
            entity.ToTable("LTS_UserCountryAccess");
            entity.HasKey(a => new { a.UserId, a.CountryId });
            entity.Ignore(a => a.Country);
        });

        builder.Entity<UserPagePermission>(entity =>
        {
            entity.ToTable("LTS_UserPagePermissions");
            entity.Property(p => p.Id).HasColumnName("ID");
            entity.Ignore(p => p.Country);
        });
    }
}

/// <summary>One row of LTS_Countries in the external LTS_Integration database.</summary>
public class LtsIntegrationCountry
{
    public int Id { get; set; }
    public required string CountryCode { get; set; }
    public required string CountryDescription { get; set; }
    public string? CustomerCode { get; set; }
    public bool IsActive { get; set; } = true;
}
