using LTS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LTS.Infrastructure.Persistence.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.Property(u => u.FullName).HasMaxLength(150).IsRequired();
        builder.Property(u => u.UserType).HasConversion<int>();
        builder.Property(u => u.CreatedBy).HasMaxLength(256);
        builder.Ignore(u => u.IsExternal);

        // LTS_Integration-only column (see LtsIntegrationDbContext) - this database has no
        // matching one, so EF must not try to select it here.
        builder.Ignore(u => u.SupplierCompanyCode);

        builder.HasOne(u => u.Partner).WithMany()
            .HasForeignKey(u => u.PartnerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(u => u.PartnerId);
    }
}

public class UserCountryAccessConfiguration : IEntityTypeConfiguration<UserCountryAccess>
{
    public void Configure(EntityTypeBuilder<UserCountryAccess> builder)
    {
        builder.HasKey(a => new { a.UserId, a.CountryId });

        builder.HasOne(a => a.User).WithMany(u => u.CountryAccess)
            .HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Country).WithMany()
            .HasForeignKey(a => a.CountryId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserPagePermissionConfiguration : IEntityTypeConfiguration<UserPagePermission>
{
    public void Configure(EntityTypeBuilder<UserPagePermission> builder)
    {
        builder.Property(p => p.PageKey).HasMaxLength(50).IsRequired();

        builder.HasOne(p => p.User).WithMany(u => u.PagePermissions)
            .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);

        // Cascading from Country as well would give SQL Server two delete paths into this
        // table, so country deletes are blocked while permissions still reference them.
        builder.HasOne(p => p.Country).WithMany()
            .HasForeignKey(p => p.CountryId).OnDelete(DeleteBehavior.Restrict);

        // One grant per page per country per user; cross-country admin pages use a null country.
        builder.HasIndex(p => new { p.UserId, p.CountryId, p.PageKey }).IsUnique();
    }
}
