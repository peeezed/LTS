using LTS.Application.DelayAlerts;
using LTS.Application.Reference;
using LTS.Domain.Entities;
using LTS.Domain.Enums;
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
    public DbSet<LtsIntegrationStore> Stores => Set<LtsIntegrationStore>();

    public DbSet<UserCountryAccess> UserCountryAccess => Set<UserCountryAccess>();
    public DbSet<UserPagePermission> UserPagePermissions => Set<UserPagePermission>();

    public DbSet<LtsIntegrationShipment> Shipments => Set<LtsIntegrationShipment>();
    public DbSet<LtsIntegrationShipmentDate> ShipmentDates => Set<LtsIntegrationShipmentDate>();
    public DbSet<LtsIntegrationShipmentTransfer> ShipmentTransfers => Set<LtsIntegrationShipmentTransfer>();
    public DbSet<LtsIntegrationShipmentTransferDate> ShipmentTransferDates => Set<LtsIntegrationShipmentTransferDate>();
    public DbSet<LtsIntegrationBox> Boxes => Set<LtsIntegrationBox>();

    public DbSet<LtsIntegrationKpiTarget> KpiTargets => Set<LtsIntegrationKpiTarget>();
    public DbSet<LtsShipmentFeedStagingRecord> ShipmentFeedStaging => Set<LtsShipmentFeedStagingRecord>();
    public DbSet<LtsIntegrationDelayAlertConfig> DelayAlertConfigs => Set<LtsIntegrationDelayAlertConfig>();
    public DbSet<LtsIntegrationMilestoneAudit> MilestoneAudits => Set<LtsIntegrationMilestoneAudit>();
    public DbSet<LtsIntegrationRomaniaOneClickToken> RomaniaOneClickTokens => Set<LtsIntegrationRomaniaOneClickToken>();

    // The seven shipment attribute lookup tables all share the same Code+Description shape, so
    // one shared-type entity (LtsIntegrationAttribute) is mapped onto each rather than declaring
    // six near-identical POCOs. LTS_ArrivalCountries is deliberately not mapped: Arrival Country
    // is resolved from the shipment's real receiving country (see
    // IntegrationShipmentQueryService.BackfillArrivalCountryAsync), not this table.
    public DbSet<LtsIntegrationAttribute> ArrivalCustomsAttributes => Set<LtsIntegrationAttribute>("ArrivalCustomsAttribute");
    public DbSet<LtsIntegrationAttribute> ExportTypeAttributes => Set<LtsIntegrationAttribute>("ExportTypeAttribute");
    public DbSet<LtsIntegrationAttribute> TransportTypeAttributes => Set<LtsIntegrationAttribute>("TransportTypeAttribute");
    public DbSet<LtsIntegrationAttribute> LoadingPointAttributes => Set<LtsIntegrationAttribute>("LoadingPointAttribute");
    public DbSet<LtsIntegrationAttribute> LogisticsCompanyAttributes => Set<LtsIntegrationAttribute>("LogisticsCompanyAttribute");
    public DbSet<LtsIntegrationAttribute> BrokerAttributes => Set<LtsIntegrationAttribute>("BrokerAttribute");

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

        builder.Entity<LtsIntegrationStore>(entity =>
        {
            entity.ToTable("LTS_Stores");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).HasColumnName("ID");
            entity.Property(s => s.Code).HasColumnName("StoreCode").HasMaxLength(50);
            entity.Property(s => s.CurrAccCode).HasColumnName("StoreCurrAccCode").HasMaxLength(50);
            entity.Property(s => s.Description).HasColumnName("StoreDescription").HasMaxLength(200);
            entity.Property(s => s.City).HasColumnName("City").HasMaxLength(100);
        });

        // AppUser.CountryAccess/PagePermissions are ignored: callers query the DbSets directly
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
            entity.Property(u => u.SupplierCompanyCode).HasColumnName("SupplierCompanyCode").HasMaxLength(50);
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
        });

        builder.Entity<UserPagePermission>(entity =>
        {
            entity.ToTable("LTS_UserPagePermissions");
            entity.Property(p => p.Id).HasColumnName("ID");
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

        builder.Entity<LtsIntegrationKpiTarget>(entity =>
        {
            entity.ToTable("LTS_KpiTargets");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Id).HasColumnName("ID");
            entity.Property(t => t.Step).HasConversion<string>().HasMaxLength(30);
        });

        // Append-only audit trail for the shipment feed: one row per raw HTTP response (the bulk
        // per-country list call, or one of the per-shipment detail calls), never updated or
        // deleted once written - see ShipmentFeedRunner. EndpointKind/Status are stored as text
        // to match this database's existing enum-as-text convention.
        builder.Entity<LtsShipmentFeedStagingRecord>(entity =>
        {
            entity.ToTable("LTS_ShipmentFeedStaging");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).HasColumnName("ID");
            entity.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        });

        builder.Entity<LtsIntegrationDelayAlertConfig>(entity =>
        {
            entity.ToTable("LTS_DelayAlertConfigs");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).HasColumnName("ID");
            entity.Property(c => c.MailKind).HasConversion<string>().HasMaxLength(20);
        });

        builder.Entity<LtsIntegrationMilestoneAudit>(entity =>
        {
            entity.ToTable("LTS_MilestoneAudit");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Id).HasColumnName("ID");
            entity.Property(a => a.MilestoneType).HasConversion<string>().HasMaxLength(50);
            entity.Property(a => a.Source).HasConversion<string>().HasMaxLength(30);
        });

        // Single-row table: the current KLG OneClick access/refresh token pair, encrypted at rest
        // via IDataProtector (see RomaniaTokenStore) since LTS_Integration is a shared database,
        // not app-private. There is only ever one active row - RomaniaTokenStore replaces it in
        // place on every refresh rather than appending history.
        builder.Entity<LtsIntegrationRomaniaOneClickToken>(entity =>
        {
            entity.ToTable("LTS_RomaniaOneClickToken");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Id).HasColumnName("ID");
        });

        foreach (var (name, table) in new[]
        {
            ("ArrivalCustomsAttribute", "LTS_ArrivalCustoms"),
            ("ExportTypeAttribute", "LTS_ExportTypes"),
            ("TransportTypeAttribute", "LTS_TransportTypes"),
            ("LoadingPointAttribute", "LTS_LoadingPoints"),
            ("LogisticsCompanyAttribute", "LTS_LogisticsCompanies"),
            ("BrokerAttribute", "LTS_Brokers")
        })
        {
            builder.SharedTypeEntity<LtsIntegrationAttribute>(name, entity =>
            {
                entity.ToTable(table);
                entity.HasKey(a => a.Id);
                entity.Property(a => a.Id).HasColumnName("ID");
            });
        }
    }
}

/// <summary>
/// One row of any of the seven shipment attribute lookup tables (LTS_ArrivalCustoms,
/// LTS_ExportTypes, LTS_TransportTypes, LTS_LoadingPoints, LTS_LogisticsCompanies, LTS_Brokers) -
/// each is just an ID/Code/Description, so one shared-type entity is mapped onto all of them.
/// </summary>
public class LtsIntegrationAttribute
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Description { get; set; }
}

/// <summary>Picks the right DbSet for an <see cref="AttributeKind"/>, shared by every caller that reads or writes one.</summary>
public static class AttributeTables
{
    public static DbSet<LtsIntegrationAttribute> For(LtsIntegrationDbContext db, AttributeKind kind) => kind switch
    {
        AttributeKind.ArrivalCustoms => db.ArrivalCustomsAttributes,
        AttributeKind.ExportType => db.ExportTypeAttributes,
        AttributeKind.TransportType => db.TransportTypeAttributes,
        AttributeKind.LoadingPoint => db.LoadingPointAttributes,
        AttributeKind.LogisticsCompany => db.LogisticsCompanyAttributes,
        AttributeKind.Broker => db.BrokerAttributes,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
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
/// One row of LTS_Stores: a transfer's final destination. CountryId is required - unlike the
/// six shipment attribute lookup tables, a store code only means something within one country.
/// City exists for the Local Transportation KPI leg, which a future round will let a target scope
/// by (see IntegrationKpiCatalog) - not read by anything yet.
///
/// CurrAccCode, Code and Description are all nullable because the shipment feed only ever knows
/// a store by its CurrAccCode (the field GetInvoiceDetailByInvoiceNumber calls "StoreCode" is
/// actually this value, not our own Code) - see ShipmentFeedRunner.EnsureStoreAsync, which creates
/// a bare CurrAccCode-only row for a store Master Data hasn't described yet, rather than letting
/// an unrecognised store block the transfer. An admin fills in Code/Description/City afterward.
/// </summary>
public class LtsIntegrationStore
{
    public int Id { get; set; }
    public required int CountryId { get; set; }
    public string? Code { get; set; }
    public string? CurrAccCode { get; set; }
    public string? Description { get; set; }
    public string? City { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// One row of LTS_Shipments: the header an integration writes for a shipment, before the app
/// reads it. CurrentStatus/Performance are the display strings from StatusDisplay
/// (TrackingStatus/PerformanceStatus.ToDisplay()), not enum names - e.g. "Not Started", not
/// "NotStarted". The six non-country attribute columns are free text matching the Description of
/// a row in the corresponding LTS_-prefixed lookup table, not that row's id - resolved by
/// IntegrationShipmentQueryService, with the raw text shown as a fallback when nothing matches.
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
/// One row of LTS_ShipmentDates: a shipment's own milestone dates, one row per ReferenceNo, plus
/// the five KPI*Date deadline columns IntegrationKpiCalculator computes and stores (each one sits
/// immediately before the actual date it gates - see IntegrationKpiCalculator's doc comment for
/// how they're derived and scored). The EstimatedDepartureDate/EstimatedArrivalDate columns the
/// table also carries are ETAs, unrelated to KPI, and are still not mapped - nothing in the app
/// reads or writes them yet.
/// </summary>
public class LtsIntegrationShipmentDate
{
    public int Id { get; set; }
    public required string ReferenceNo { get; set; }
    public DateOnly? LoadingDate { get; set; }
    public DateOnly? KPICustomsClearanceDate { get; set; }
    public DateOnly? CustomsClearanceDate { get; set; }
    public DateOnly? KPIDepartureDate { get; set; }
    public DateOnly? DepartureDate { get; set; }
    public DateOnly? KPIArrivalToDestinationDate { get; set; }
    public DateOnly? ArrivalDate { get; set; }
    public DateOnly? ArrivalCustomsStartDate { get; set; }
    public DateOnly? KPIArrivalCustomsEndDate { get; set; }
    public DateOnly? ArrivalCustomsEndDate { get; set; }
    public DateOnly? KPILeadTimeToXdock { get; set; }
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

    /// <summary>
    /// KLG OneClick's "perm_shipment_id" for this transfer - one KLG domestic shipment maps to one
    /// LTS transfer, not a whole LTS shipment. Romania-only; typed in by hand on Transfers.razor.
    /// Null for every non-Romania transfer, and for a Romania one not yet linked.
    /// </summary>
    public string? RomaniaPermShipmentId { get; set; }
}

/// <summary>
/// One row of LTS_ShipmentTransferDates: a transfer's crossdock and store dates, plus
/// KPICrossdockDepartureDate - the deadline for this transfer's own CrossdockDepartureDate, the
/// one KPI leg (XDock) whose start is on the shipment but whose end is on the transfer - and
/// KPILocalTransportation, the deadline for StoreArrivalDate, a leg fully contained on the
/// transfer (Crossdock Departure to Store Arrival). Declared here between CrossdockDepartureDate
/// and PlannedStoreArrivalDate to mirror the timeline, though ALTER TABLE ADD always appends the
/// column physically at the end of the live table regardless of this declaration order.
/// </summary>
public class LtsIntegrationShipmentTransferDate
{
    public required string TransferNo { get; set; }
    public DateOnly? KPICrossdockDepartureDate { get; set; }
    public DateOnly? CrossdockDepartureDate { get; set; }
    public DateOnly? KPILocalTransportation { get; set; }
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

/// <summary>
/// One row of LTS_KpiTargets: a target duration in days for one IntegrationKpiStep, required to
/// belong to exactly one country (every country has its own KPI values) and optionally scoped
/// further by up to four shipment attributes - a null column means "any". ExportType/LoadingPoint/
/// ArrivalCustoms/TransportType store Description text, matching how LtsIntegrationShipment's own
/// attribute columns are stored (see its doc comment) - not a Code, so matching a shipment to a
/// target is a direct string comparison with no resolution step. No effective-dating: editing a
/// target only affects legs whose start milestone is entered afterward (see IntegrationKpiCalculator).
/// </summary>
public class LtsIntegrationKpiTarget
{
    public int Id { get; set; }
    public required int CountryId { get; set; }
    public required IntegrationKpiStep Step { get; set; }
    public string? ExportType { get; set; }
    public string? LoadingPoint { get; set; }
    public string? ArrivalCustoms { get; set; }
    public string? TransportType { get; set; }
    public required int TargetDays { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// One row of LTS_ShipmentFeedStaging: the raw, verbatim JSON of one HTTP response from the
/// shipment feed - either the bulk per-country list call (ReferenceNo null, EndpointKind
/// "List") or one of the per-shipment detail calls (ReferenceNo set). Kept forever as an audit
/// trail regardless of how standardizing/pushing it turned out - see ShipmentFeedRunner.
/// </summary>
public class LtsShipmentFeedStagingRecord
{
    public int Id { get; set; }
    public required string CustomerCode { get; set; }
    public string? CountryCode { get; set; }
    public required string EndpointKind { get; set; }
    public DateTime FetchedAt { get; set; }
    public string? ReferenceNo { get; set; }
    public required string RawPayload { get; set; }
    public ShipmentFeedStagingStatus Status { get; set; } = ShipmentFeedStagingStatus.Pending;
    public DateTime? ProcessedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum ShipmentFeedStagingStatus { Pending, Processed, Failed }

/// <summary>
/// One row of LTS_DelayAlertConfigs: one country's settings for one of the two delay alert mails
/// (Shipment or Transfer) - who receives it, what time of day it goes out, and its subject/body.
/// Exactly one send per day per config (SendTime, not a list) - the simplest shape covering "a
/// daily digest"; see DelayAlertRunner for how LastSentDate prevents a double-send the same day.
/// </summary>
public class LtsIntegrationDelayAlertConfig
{
    public int Id { get; set; }
    public required int CountryId { get; set; }
    public required DelayAlertMailKind MailKind { get; set; }
    public bool IsEnabled { get; set; }
    public string? Recipients { get; set; }
    public TimeOnly SendTime { get; set; } = new(8, 0);
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public DateOnly? LastSentDate { get; set; }
}

/// <summary>
/// One row of LTS_MilestoneAudit: one recorded change to a milestone date, written by
/// IntegrationMilestoneService.ApplyAsync. Keyed by ReferenceNo/TransferNo directly (natural
/// keys, matching how every other LTS_Integration table already works) rather than the old
/// LtsDbContext MilestoneAudit's surrogate Scope+EntityId+ShipmentId columns, which existed only
/// because that database's Shipments/Transfers were themselves int-keyed. TransferNo is null for
/// a shipment-scope change - there's no separate Scope column since it's implied by that.
/// </summary>
public class LtsIntegrationMilestoneAudit
{
    public int Id { get; set; }
    public required string ReferenceNo { get; set; }
    public string? TransferNo { get; set; }
    public required MilestoneType MilestoneType { get; set; }
    public DateOnly? OldValue { get; set; }
    public DateOnly? NewValue { get; set; }
    public required MilestoneSource Source { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public int? PartnerId { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// One row of LTS_RomaniaOneClickToken: the current KLG access/refresh token pair, encrypted at
/// rest (see RomaniaTokenStore). Single-row table - RefreshAsync overwrites the same row every
/// time rather than appending a history, since only the most recent pair is ever valid (KLG
/// invalidates the previous pair on every refresh). RefreshTokenExpiresAtUtc is informational only
/// (KLG's refresh response never returns a refresh-token lifetime, only the access token's
/// expires_in) - computed from the documented fixed 30-day rotation window, not a live value.
/// </summary>
public class LtsIntegrationRomaniaOneClickToken
{
    public int Id { get; set; }
    public required string EncryptedAccessToken { get; set; }
    public required string EncryptedRefreshToken { get; set; }
    public DateTime AccessTokenExpiresAtUtc { get; set; }
    public DateTime RefreshTokenExpiresAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
