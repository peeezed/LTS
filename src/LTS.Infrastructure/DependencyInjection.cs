using LTS.Application.Abstractions;
using LTS.Application.Excel;
using LTS.Application.Kpi;
using LTS.Application.Reference;
using LTS.Application.RomaniaOneClick;
using LTS.Application.Security;
using LTS.Application.Tracking;
using LTS.Application.DelayAlerts;
using LTS.Domain.Entities;
using LTS.Infrastructure.DelayAlerts;
using LTS.Infrastructure.Email;
using LTS.Infrastructure.ExportAttributeFeed;
using LTS.Infrastructure.Kpi;
using LTS.Infrastructure.Persistence;
using LTS.Infrastructure.Reference;
using LTS.Infrastructure.RomaniaOneClick;
using LTS.Infrastructure.Security;
using LTS.Infrastructure.ShipmentFeed;
using LTS.Infrastructure.Tracking;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LTS.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the database, Identity and the application services. The web host adds an
    /// <see cref="ICurrentUser"/> backed by the signed-in principal on top of this.
    /// </summary>
    public static IServiceCollection AddLtsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // The external database the app runs on. Its schema is managed by hand, so this is never
        // given a migrations assembly.
        var integrationConnectionString = configuration.GetConnectionString("LtsIntegration")
            ?? throw new InvalidOperationException("Connection string 'LtsIntegration' is not configured.");

        // Registered once, as a factory: IDbContextFactory<LtsIntegrationDbContext> (singleton)
        // creates short-lived instances on demand for the services below, and the scoped
        // LtsIntegrationDbContext Identity needs is derived from that same factory rather than
        // configured a second time - two separate UseSqlServer registrations for the same
        // context type conflict at startup (the factory ends up depending on the other
        // registration's scoped DbContextOptions). Identity keeps its scoped instance for the
        // whole request/circuit; the services below instead take a fresh instance per call,
        // because they used to share the scoped instance with Identity's own store, which
        // occasionally raced with it ("a second operation was started on this context instance
        // before a previous operation completed") - Blazor Server can genuinely run more than one
        // thing concurrently within the same circuit's DI scope.
        services.AddDbContextFactory<LtsIntegrationDbContext>(options => options.UseSqlServer(integrationConnectionString));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<LtsIntegrationDbContext>>().CreateDbContext());

        services.AddMemoryCache();

        services.AddIdentityCore<AppUser>(options =>
            {
                // Accounts are created by an administrator, so there is no confirmation email to send.
                options.SignIn.RequireConfirmedAccount = false;
                options.User.RequireUniqueEmail = true;

                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = false;

                // External partners share support desks; locking out on repeated failures is
                // the cheapest defence available here.
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            // Accounts sign in against LTS_Integration now, not the app's own database - see
            // LtsIntegrationDbContext.
            .AddEntityFrameworkStores<LtsIntegrationDbContext>()
            .AddSignInManager()
            .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>()
            .AddDefaultTokenProviders();

        services.AddSingleton<IClock, SystemClock>();

        // Replaced in the web host by the signed-in user; this default is what background work
        // such as the feed pollers run as.
        services.AddScoped<ICurrentUser, SystemCurrentUser>();

        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IReferenceDataService, ReferenceDataService>();
        services.AddScoped<IShipmentQueryService, IntegrationShipmentQueryService>();
        // The Shipment Details page's and the Excel upload's shared writer.
        services.AddScoped<IIntegrationMilestoneService, IntegrationMilestoneService>();

        // Pure application services with no database of their own.
        services.AddScoped<IDateImportService, DateImportService>();

        // Administration.
        services.AddScoped<IIntegrationKpiAdminService, IntegrationKpiAdminService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IMasterDataService, MasterDataService>();
        services.AddScoped<IIntegrationAuditQueryService, IntegrationAuditQueryService>();

        services.AddHttpClient();

        // Encrypts the Romania KLG token pair at rest (RomaniaTokenStore) - LTS_Integration is a
        // shared database, not app-private, so the live token pair is never written there in
        // plaintext. Keys default to the local file system, fine for a single app instance.
        services.AddDataProtection();

        // Shipments feed: the company's own internal shipment header source. One shared
        // endpoint, one country loop, config-driven.
        services.AddScoped<IShipmentFeedClient, ShipmentFeedClient>();
        services.AddScoped<ShipmentFeedRunner>();
        services.AddHostedService<ShipmentFeedPoller>();

        // Export attribute backfill: finds shipments missing a required KPI-scoping attribute and
        // pulls them from GetLTSExportFileDetail by reference number, then re-scores KPI once
        // anything it fetched is applied - independent of the shipments feed above (different
        // endpoint, different trigger condition, its own poll cycle).
        services.AddScoped<IExportAttributeFeedClient, ExportAttributeFeedClient>();
        services.AddScoped<ExportAttributeFeedRunner>();
        services.AddHostedService<ExportAttributeFeedPoller>();

        // Catches up LTS_Shipments.CurrentStatus for shipments whose transfer dates changed
        // outside the app (e.g. a future supplier integration writing straight into
        // LTS_ShipmentTransferDates) - see ShipmentStatusReconciler.
        services.AddScoped<ShipmentStatusReconciler>();
        services.AddHostedService<ShipmentStatusReconciliationPoller>();

        // Delay alert mails: two Excel-attached reports (shipments short of Crossdock Arrival,
        // transfers short of Store Arrival), configured per country in the admin page and sent on
        // each config's own daily SendTime - see DelayAlertRunner/DelayAlertReportBuilder.
        services.AddScoped<IEmailSender, MailKitEmailSender>();
        services.AddScoped<IDelayAlertAdminService, DelayAlertAdminService>();
        services.AddScoped<DelayAlertRunner>();
        services.AddHostedService<DelayAlertPoller>();

        // Romania: KLG OneClick, a genuine third-party API with OAuth-style rotating refresh
        // tokens (unlike every other integration here, which uses a static bearer secret). One
        // KLG "domestic shipment" is one LTS transfer - looked up individually by the
        // RomaniaPermShipmentId typed onto it (see Transfers.razor), never listed in bulk.
        services.AddScoped<IRomaniaTokenStore, RomaniaTokenStore>();
        services.AddScoped<IRomaniaOneClickClient, RomaniaOneClickClient>();
        services.AddScoped<IRomaniaTransferLinkService, RomaniaTransferLinkService>();
        services.AddScoped<RomaniaShipmentFeedRunner>();
        services.AddHostedService<RomaniaShipmentPoller>();

        return services;
    }
}
