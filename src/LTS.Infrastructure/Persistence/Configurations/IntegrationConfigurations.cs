using LTS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LTS.Infrastructure.Persistence.Configurations;

public class IntegrationSourceConfiguration : IEntityTypeConfiguration<IntegrationSource>
{
    public void Configure(EntityTypeBuilder<IntegrationSource> builder)
    {
        builder.Property(s => s.Kind).HasConversion<int>();
        builder.Property(s => s.Name).HasMaxLength(150).IsRequired();
        builder.Property(s => s.AdapterKey).HasMaxLength(50).IsRequired();
        builder.Property(s => s.BaseUrl).HasMaxLength(500);
        builder.Property(s => s.SecretName).HasMaxLength(100);
        builder.Property(s => s.Cursor).HasMaxLength(200);
        builder.Property(s => s.CreatedBy).HasMaxLength(256);
        builder.Property(s => s.UpdatedBy).HasMaxLength(256);

        builder.HasOne(s => s.Country).WithMany()
            .HasForeignKey(s => s.CountryId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.CountryId, s.Kind });
    }
}

public class StatusMappingConfiguration : IEntityTypeConfiguration<StatusMapping>
{
    public void Configure(EntityTypeBuilder<StatusMapping> builder)
    {
        builder.Property(m => m.RawCode).HasMaxLength(100).IsRequired();
        builder.Property(m => m.RawDescription).HasMaxLength(250);
        builder.Property(m => m.MilestoneType).HasConversion<int?>();
        builder.Property(m => m.CreatedBy).HasMaxLength(256);
        builder.Property(m => m.UpdatedBy).HasMaxLength(256);

        builder.HasOne(m => m.IntegrationSource).WithMany()
            .HasForeignKey(m => m.IntegrationSourceId).OnDelete(DeleteBehavior.Cascade);

        // A source cannot map the same raw code twice, which is what makes lookups unambiguous.
        builder.HasIndex(m => new { m.IntegrationSourceId, m.RawCode }).IsUnique();
    }
}

public class IntegrationRunConfiguration : IEntityTypeConfiguration<IntegrationRun>
{
    public void Configure(EntityTypeBuilder<IntegrationRun> builder)
    {
        builder.Property(r => r.Status).HasConversion<int>();
        builder.Property(r => r.ErrorMessage).HasMaxLength(2000);
        builder.Ignore(r => r.Duration);

        builder.HasOne(r => r.IntegrationSource).WithMany()
            .HasForeignKey(r => r.IntegrationSourceId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.IntegrationSourceId, r.StartedAt });
    }
}

public class IntegrationMessageConfiguration : IEntityTypeConfiguration<IntegrationMessage>
{
    public void Configure(EntityTypeBuilder<IntegrationMessage> builder)
    {
        builder.Property(m => m.Status).HasConversion<int>();
        builder.Property(m => m.ExternalId).HasMaxLength(100);
        builder.Property(m => m.EntityReference).HasMaxLength(100);
        builder.Property(m => m.RawStatusCode).HasMaxLength(100);
        builder.Property(m => m.ErrorMessage).HasMaxLength(2000);

        builder.HasOne(m => m.IntegrationRun).WithMany(r => r.Messages)
            .HasForeignKey(m => m.IntegrationRunId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => m.EntityReference);
        builder.HasIndex(m => new { m.IntegrationRunId, m.Status });
    }
}
