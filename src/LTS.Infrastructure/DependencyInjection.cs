using LTS.Application.Abstractions;
using LTS.Application.Excel;
using LTS.Application.Integration;
using LTS.Application.Kpi;
using LTS.Application.Reference;
using LTS.Application.Security;
using LTS.Application.Tracking;
using LTS.Domain.Entities;
using LTS.Infrastructure.Integration;
using LTS.Infrastructure.Kpi;
using LTS.Infrastructure.Persistence;
using LTS.Infrastructure.Reference;
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
        var connectionString = configuration.GetConnectionString("Lts")
            ?? throw new InvalidOperationException("Connection string 'Lts' is not configured.");

        services.AddDbContext<LtsDbContext>(options => options.UseSqlServer(
            connectionString,
            sql => sql.MigrationsAssembly(typeof(LtsDbContext).Assembly.FullName)));

        // The external database the app is being migrated onto, table by table. Its schema is
        // managed by hand, so this is never given a migrations assembly.
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
        // such as the integration poller runs as.
        services.AddScoped<ICurrentUser, SystemCurrentUser>();

        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IReferenceDataService, ReferenceDataService>();
        // Sourced from LTS_Integration, not the old database - see IntegrationShipmentQueryService.
        services.AddScoped<IShipmentQueryService, IntegrationShipmentQueryService>();
        // The old database's own milestone writer - still used by the integration poller
        // (IntegrationRunner) and the Excel upload (DateImportService), both of which still act
        // on LtsDbContext's own Shipments and are out of scope for this migration for now.
        services.AddScoped<IMilestoneService, MilestoneService>();
        // The Shipment Details page's writer, kept separate rather than replacing the
        // registration above: IntegrationRunner also depends on IMilestoneService, and swapping
        // it would silently break the (explicitly out-of-scope) integration poller, whose
        // shipments only exist in the old database.
        services.AddScoped<IIntegrationMilestoneService, IntegrationMilestoneService>();
        services.AddScoped<IKpiTargetProvider, KpiTargetProvider>();

        // Pure application services with no database of their own.
        services.AddScoped<IDateImportService, DateImportService>();

        // Administration.
        services.AddScoped<IKpiAdminService, KpiAdminService>();
        services.AddScoped<IIntegrationKpiAdminService, IntegrationKpiAdminService>();
        services.AddScoped<IIntegrationAdminService, IntegrationAdminService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IMasterDataService, MasterDataService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();

        // Integration: adapters are registered by key, so onboarding a country adds an adapter
        // here and its configuration rows in the database - nothing else changes.
        services.AddHttpClient();
        services.AddSingleton<ICountryIntegrationAdapter, MockJsonAdapter>();
        services.AddSingleton<IIntegrationAdapterRegistry, IntegrationAdapterRegistry>();
        services.AddScoped<IntegrationRunner>();
        services.AddHostedService<IntegrationPoller>();

        // Shipments feed: the company's own internal shipment header source. One shared
        // endpoint, one country loop, config-driven - deliberately not routed through the dead
        // adapter registry above.
        services.AddScoped<IShipmentFeedClient, ShipmentFeedClient>();
        services.AddScoped<ShipmentFeedRunner>();
        services.AddHostedService<ShipmentFeedPoller>();

        // Catches up LTS_Shipments.CurrentStatus for shipments whose transfer dates changed
        // outside the app (e.g. a future supplier integration writing straight into
        // LTS_ShipmentTransferDates) - see ShipmentStatusReconciler.
        services.AddScoped<ShipmentStatusReconciler>();
        services.AddHostedService<ShipmentStatusReconciliationPoller>();

        return services;
    }
}
