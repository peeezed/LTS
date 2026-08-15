using LTS.Domain.Entities;
using LTS.Domain.Enums;
using LTS.Infrastructure.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LTS.Infrastructure.Persistence;

/// <summary>
/// Brings the database up to date at startup and makes sure there is always someone who can
/// log in — an empty LTS with no administrator would need a manual SQL insert to become usable.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitialiseDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;

        var options = provider.GetRequiredService<IOptions<LtsOptions>>().Value;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("LTS.Startup");
        var db = provider.GetRequiredService<LtsDbContext>();

        if (options.ApplyMigrationsOnStartup)
        {
            logger.LogInformation("Applying database migrations...");
            await db.Database.MigrateAsync();
        }

        await SeedAdministratorAsync(provider, options, logger);

        if (options.SeedDemoData)
        {
            await DemoDataSeeder.SeedAsync(provider, logger);
        }
    }

    private static async Task SeedAdministratorAsync(
        IServiceProvider provider, LtsOptions options, ILogger logger)
    {
        var users = provider.GetRequiredService<UserManager<AppUser>>();

        if (await users.Users.AnyAsync(u => u.UserType == UserType.Admin))
        {
            return;
        }

        var admin = new AppUser
        {
            UserName = options.Admin.Email,
            Email = options.Admin.Email,
            EmailConfirmed = true,
            FullName = options.Admin.FullName,
            UserType = UserType.Admin,
            IsActive = true,
            // The seeded password is a bootstrap credential, not a usable account password.
            MustChangePassword = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "system"
        };

        var result = await users.CreateAsync(admin, options.Admin.InitialPassword);

        if (result.Succeeded)
        {
            logger.LogWarning(
                "Created the first administrator '{Email}'. Sign in and change the password immediately.",
                options.Admin.Email);
            return;
        }

        logger.LogError("Could not create the initial administrator: {Errors}",
            string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
