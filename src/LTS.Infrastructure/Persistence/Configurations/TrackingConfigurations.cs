using LTS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LTS.Infrastructure.Persistence.Configurations;

public class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.Property(s => s.ReferenceNo).HasMaxLength(50).IsRequired();
        builder.Property(s => s.InvoiceNo).HasMaxLength(50).IsRequired();
        builder.Property(s => s.CurrentStatus).HasConversion<int>();
        builder.Property(s => s.Performance).HasConversion<int>();
        builder.Property(s => s.CreatedBy).HasMaxLength(256);
        builder.Property(s => s.UpdatedBy).HasMaxLength(256);
        builder.Ignore(s => s.LoadingCountryCode);

        // Unique system-wide, not per country: one reference number is one shipment, so
        // uploads and integration payloads can identify it without knowing the country.
        builder.HasIndex(s => s.ReferenceNo).IsUnique();
        builder.HasIndex(s => s.InvoiceNo).IsUnique();

        // Every grid query filters by country first, then by status or performance.
        builder.HasIndex(s => new { s.ArrivalCountryId, s.CurrentStatus });
        builder.HasIndex(s => new { s.ArrivalCountryId, s.Performance });

        // External users' rows are filtered to their own partner.
        builder.HasIndex(s => new { s.ArrivalCountryId, s.BrokerId });
        builder.HasIndex(s => new { s.ArrivalCountryId, s.LogisticsCompanyId });

        builder.HasOne(s => s.ArrivalCountry).WithMany()
            .HasForeignKey(s => s.ArrivalCountryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.ArrivalCustoms).WithMany()
            .HasForeignKey(s => s.ArrivalCustomsId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.ExportType).WithMany()
            .HasForeignKey(s => s.ExportTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.TransportType).WithMany()
            .HasForeignKey(s => s.TransportTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.LoadingPoint).WithMany()
            .HasForeignKey(s => s.LoadingPointId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.LogisticsCompany).WithMany()
            .HasForeignKey(s => s.LogisticsCompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Broker).WithMany()
            .HasForeignKey(s => s.BrokerId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class TransferConfiguration : IEntityTypeConfiguration<Transfer>
{
    public void Configure(EntityTypeBuilder<Transfer> builder)
    {
        builder.Property(t => t.TransferNo).HasMaxLength(100).IsRequired();
        builder.Property(t => t.CurrentStatus).HasConversion<int>();
        builder.Property(t => t.Performance).HasConversion<int>();
        builder.Property(t => t.CreatedBy).HasMaxLength(256);
        builder.Property(t => t.UpdatedBy).HasMaxLength(256);

        builder.HasIndex(t => t.TransferNo).IsUnique();
        builder.HasIndex(t => t.ShipmentId);

        // "Shipments On The Way" is everything without a store arrival date, so that column
        // is filtered on constantly.
        builder.HasIndex(t => new { t.StoreId, t.StoreArrivalDate });

        builder.HasOne(t => t.Shipment)
            .WithMany(s => s.Transfers)
            .HasForeignKey(t => t.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Store).WithMany()
            .HasForeignKey(t => t.StoreId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class MilestoneAuditConfiguration : IEntityTypeConfiguration<MilestoneAudit>
{
    public void Configure(EntityTypeBuilder<MilestoneAudit> builder)
    {
        builder.Property(a => a.Scope).HasConversion<int>();
        builder.Property(a => a.MilestoneType).HasConversion<int>();
        builder.Property(a => a.Source).HasConversion<int>();
        builder.Property(a => a.UserName).HasMaxLength(256);
        builder.Property(a => a.Note).HasMaxLength(500);

        // The whole history of a reference number, newest first.
        builder.HasIndex(a => new { a.ShipmentId, a.ChangedAt });
        builder.HasIndex(a => new { a.Scope, a.EntityId });
        builder.HasIndex(a => a.ChangedAt);
    }
}
