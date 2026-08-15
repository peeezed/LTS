using LTS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LTS.Infrastructure.Persistence.Configurations;

public class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.Property(c => c.Code).HasMaxLength(2).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(c => c.Code).IsUnique();
    }
}

public class PartnerConfiguration : IEntityTypeConfiguration<Partner>
{
    public void Configure(EntityTypeBuilder<Partner> builder)
    {
        builder.Property(p => p.Code).HasMaxLength(30).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(150).IsRequired();
        builder.Property(p => p.Type).HasConversion<int>();

        // Codes only have to be unique within a type: the same company can act as both a
        // logistics company and a broker without the codes colliding.
        builder.HasIndex(p => new { p.Type, p.Code }).IsUnique();
    }
}

public class LoadingPointConfiguration : IEntityTypeConfiguration<LoadingPoint>
{
    public void Configure(EntityTypeBuilder<LoadingPoint> builder)
    {
        builder.Property(l => l.Code).HasMaxLength(30).IsRequired();
        builder.Property(l => l.Name).HasMaxLength(150).IsRequired();
        builder.Property(l => l.CountryCode).HasMaxLength(2).IsRequired();
        builder.HasIndex(l => l.Code).IsUnique();
        builder.HasIndex(l => l.CountryCode);
    }
}

public class LookupValueConfiguration : IEntityTypeConfiguration<LookupValue>
{
    public void Configure(EntityTypeBuilder<LookupValue> builder)
    {
        builder.Property(l => l.Kind).HasConversion<int>();
        builder.Property(l => l.Code).HasMaxLength(50).IsRequired();
        builder.Property(l => l.Name).HasMaxLength(150).IsRequired();

        builder.HasOne(l => l.Country)
            .WithMany()
            .HasForeignKey(l => l.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => new { l.Kind, l.CountryId, l.Code }).IsUnique();
    }
}

public class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> builder)
    {
        builder.Property(s => s.Code).HasMaxLength(30).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(150).IsRequired();
        builder.Ignore(s => s.DisplayName);

        builder.HasOne(s => s.Country)
            .WithMany(c => c.Stores)
            .HasForeignKey(s => s.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.CountryId, s.Code }).IsUnique();
    }
}
