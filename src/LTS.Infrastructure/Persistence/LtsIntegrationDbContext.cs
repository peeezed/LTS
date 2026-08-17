using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Persistence;

/// <summary>
/// Read access to LTS_Integration, the external database the app is being migrated onto one
/// table at a time. Its schema is managed by hand, not by EF migrations, so this context only
/// ever maps tables that already exist - it never creates or migrates anything.
/// </summary>
public class LtsIntegrationDbContext(DbContextOptions<LtsIntegrationDbContext> options) : DbContext(options)
{
    public DbSet<LtsIntegrationCountry> Countries => Set<LtsIntegrationCountry>();

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
}
