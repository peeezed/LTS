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

    public DbSet<LtsIntegrationShipment> Shipments => Set<LtsIntegrationShipment>();
    public DbSet<LtsIntegrationShipmentDate> ShipmentDates => Set<LtsIntegrationShipmentDate>();
    public DbSet<LtsIntegrationShipmentTransfer> ShipmentTransfers => Set<LtsIntegrationShipmentTransfer>();
    public DbSet<LtsIntegrationShipmentTransferDate> ShipmentTransferDates => Set<LtsIntegrationShipmentTransferDate>();
    public DbSet<LtsIntegrationBox> Boxes => Set<LtsIntegrationBox>();

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

        // The staging tables an integration writes a shipment into before the LTS frontend reads
        // it: one header row per shipment, its dates, its transfers (split at the crossdock, one
        // per store) and their dates, and the boxes each transfer contains. None of these carry
        // real foreign keys or a country id - a shipment's country is resolved by matching its
        // CustomerCode against LTS_Countries.CustomerCode (see IntegrationShipmentQueryService).
        // None of the four child tables have a declared primary key in the hand-written DDL, so
        // the composite keys below exist only for EF's model (not enforced in SQL) - fine for the
        // read-only, AsNoTracking queries this context is used for here.
        builder.Entity<LtsIntegrationShipment>(entity =>
        {
            entity.ToTable("LTS_Shipments");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).HasColumnName("ID");
        });

        builder.Entity<LtsIntegrationShipmentDate>(entity =>
        {
            entity.ToTable("LTS_ShipmentDates");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Id).HasColumnName("ID");
        });

        builder.Entity<LtsIntegrationShipmentTransfer>(entity =>
        {
            entity.ToTable("LTS_ShipmentTransfers");
            entity.HasKey(t => new { t.ReferenceNo, t.TransferNo });
        });

        builder.Entity<LtsIntegrationShipmentTransferDate>(entity =>
        {
            entity.ToTable("LTS_ShipmentTransferDates");
            entity.HasKey(d => d.TransferNo);
        });

        builder.Entity<LtsIntegrationBox>(entity =>
        {
            entity.ToTable("LTS_Boxes");
            entity.HasKey(b => new { b.TransferNo, b.PackageNo });
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

/// <summary>
/// One row of LTS_Shipments: the header an integration writes for a shipment, before the app
/// reads it. CurrentStatus/Performance are the display strings from StatusDisplay
/// (TrackingStatus/PerformanceStatus.ToDisplay()), not enum names - e.g. "Not Started", not
/// "NotStarted". The seven attribute columns are free text, not ids into the LTS_-prefixed
/// attribute tables, so they are shown as-is rather than joined.
/// </summary>
public class LtsIntegrationShipment
{
    public int Id { get; set; }
    public required string ReferenceNo { get; set; }
    public required string InvoiceNo { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public required string CurrentStatus { get; set; }
    public required string Performance { get; set; }
    public string? ArrivalCountry { get; set; }
    public string? ArrivalCustoms { get; set; }
    public string? ExportType { get; set; }
    public string? TransportType { get; set; }
    public string? LoadingPoint { get; set; }
    public string? LogisticsCompany { get; set; }
    public string? BrokerCompany { get; set; }
    public required string CustomerCode { get; set; }
    public int? TotalTransfers { get; set; }
    public int? TotalBoxes { get; set; }
    public int? TotalItems { get; set; }
}

/// <summary>
/// One row of LTS_ShipmentDates: a shipment's own milestone dates, one row per ReferenceNo. Only
/// the actual dates are mapped, not the KPI target/estimate columns the table also carries -
/// KPI is out of scope for LTS_Integration.
/// </summary>
public class LtsIntegrationShipmentDate
{
    public int Id { get; set; }
    public required string ReferenceNo { get; set; }
    public DateOnly? LoadingDate { get; set; }
    public DateOnly? CustomsClearanceDate { get; set; }
    public DateOnly? DepartureDate { get; set; }
    public DateOnly? ArrivalDate { get; set; }
    public DateOnly? ArrivalCustomsStartDate { get; set; }
    public DateOnly? ArrivalCustomsEndDate { get; set; }
    public DateOnly? CrossdockArrivalDate { get; set; }
}

/// <summary>One row of LTS_ShipmentTransfers: a shipment split at the crossdock, one per store.</summary>
public class LtsIntegrationShipmentTransfer
{
    public required string ReferenceNo { get; set; }
    public required string TransferNo { get; set; }
    public string? InvoiceNo { get; set; }
    public DateOnly? DateCreated { get; set; }
    public string? ReceivingStoreCode { get; set; }
    public required string CurrentStatus { get; set; }
    public required string Performance { get; set; }
    public int? TotalBoxes { get; set; }
    public int? TotalItems { get; set; }
}

/// <summary>One row of LTS_ShipmentTransferDates: a transfer's crossdock and store dates.</summary>
public class LtsIntegrationShipmentTransferDate
{
    public required string TransferNo { get; set; }
    public DateOnly? CrossdockDepartureDate { get; set; }
    public DateOnly? PlannedStoreArrivalDate { get; set; }
    public DateOnly? StoreArrivalDate { get; set; }
}

/// <summary>
/// One row of LTS_Boxes. Store pre-acceptance and acceptance are recorded per box, not per
/// transfer - IntegrationShipmentQueryService rolls them up to a single transfer-level date.
/// </summary>
public class LtsIntegrationBox
{
    public required string TransferNo { get; set; }
    public required string PackageNo { get; set; }
    public required string Status { get; set; }
    public DateOnly? PreAcceptanceDate { get; set; }
    public DateOnly? AcceptanceDate { get; set; }
}
