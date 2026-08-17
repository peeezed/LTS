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

        services.AddDbContext<LtsIntegrationDbContext>(options => options.UseSqlServer(integrationConnectionString));

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
        services.AddScoped<IShipmentQueryService, ShipmentQueryService>();
        services.AddScoped<IMilestoneService, MilestoneService>();
        services.AddScoped<IKpiTargetProvider, KpiTargetProvider>();

        // Pure application services with no database of their own.
        services.AddScoped<IDateImportService, DateImportService>();

        // Administration.
        services.AddScoped<IKpiAdminService, KpiAdminService>();
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

        return services;
    }
}
