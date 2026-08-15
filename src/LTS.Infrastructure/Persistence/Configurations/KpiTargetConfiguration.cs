using LTS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LTS.Infrastructure.Persistence.Configurations;

public class KpiTargetConfiguration : IEntityTypeConfiguration<KpiTarget>
{
    public void Configure(EntityTypeBuilder<KpiTarget> builder)
    {
        builder.Property(k => k.Step).HasConversion<int>();
        builder.Property(k => k.LoadingCountryCode).HasMaxLength(2);
        builder.Property(k => k.CreatedBy).HasMaxLength(256);
        builder.Property(k => k.UpdatedBy).HasMaxLength(256);
        builder.Ignore(k => k.Specificity);

        builder.HasOne(k => k.ExportType).WithMany()
            .HasForeignKey(k => k.ExportTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(k => k.ArrivalCountry).WithMany()
            .HasForeignKey(k => k.ArrivalCountryId).OnDelete(DeleteBehavior.Restrict);

        // One target per key combination per effective date, so an Excel re-import updates the
        // existing row instead of quietly creating a duplicate that shadows it.
        builder.HasIndex(k => new
        {
            k.Step,
            k.ExportTypeId,
            k.LoadingCountryCode,
            k.ArrivalCountryId,
            k.EffectiveFrom
        }).IsUnique();

        builder.HasIndex(k => k.Step);
    }
}
