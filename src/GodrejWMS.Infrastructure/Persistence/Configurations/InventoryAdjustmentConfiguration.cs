using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public sealed class InventoryAdjustmentConfiguration : IEntityTypeConfiguration<InventoryAdjustment>
{
    public void Configure(EntityTypeBuilder<InventoryAdjustment> builder)
    {
        builder.ToTable("InventoryAdjustments");
        builder.HasIndex(a => a.AdjustmentNumber).IsUnique();
        builder.HasIndex(a => a.OpenStockBatchId).IsUnique();
        builder.HasIndex(a => new { a.WarehouseId, a.Status, a.RequestedAt });
        builder.Property(a => a.AdjustmentNumber).HasMaxLength(40).IsRequired();
        builder.Property(a => a.ReasonCode).HasMaxLength(50).IsRequired();
        builder.Property(a => a.ReasonText).HasMaxLength(500);
        builder.Property(a => a.RequestedByUserId).HasMaxLength(450);
        builder.Property(a => a.RequestedByUserName).HasMaxLength(256);
        builder.Property(a => a.ReviewedByUserId).HasMaxLength(450);
        builder.Property(a => a.ReviewedByUserName).HasMaxLength(256);
        builder.Property(a => a.ReviewComment).HasMaxLength(500);
        builder.Property(a => a.CreatedByUserId).HasMaxLength(450);
        builder.Property(a => a.UpdatedByUserId).HasMaxLength(450);
        builder.Property(a => a.RowVersion).IsConcurrencyToken();

        builder.HasOne(a => a.Warehouse).WithMany().HasForeignKey(a => a.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(a => a.Lines).WithOne(l => l.InventoryAdjustment).HasForeignKey(l => l.InventoryAdjustmentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class InventoryAdjustmentLineConfiguration : IEntityTypeConfiguration<InventoryAdjustmentLine>
{
    public void Configure(EntityTypeBuilder<InventoryAdjustmentLine> builder)
    {
        builder.ToTable("InventoryAdjustmentLines");
        builder.HasIndex(l => l.StockBatchId);
        builder.HasIndex(l => new { l.MaterialId, l.PalletPositionId, l.MfgMonth });
        builder.Property(l => l.SystemQuantityAtCount).HasPrecision(14, 3);
        builder.Property(l => l.CountedQuantity).HasPrecision(14, 3);
        builder.Property(l => l.VarianceQuantity).HasPrecision(14, 3);
        builder.Property(l => l.ActualQuantityBeforePosting).HasPrecision(14, 3);
        builder.Property(l => l.ActualQuantityAfterPosting).HasPrecision(14, 3);
        builder.Property(l => l.PhysicalSystemQuantityAtCount).HasPrecision(14, 3);
        builder.Property(l => l.ActualPhysicalQuantityBeforePosting).HasPrecision(14, 3);
        builder.Property(l => l.CreatedByUserId).HasMaxLength(450);
        builder.Property(l => l.UpdatedByUserId).HasMaxLength(450);

        builder.HasOne(l => l.StockBatch).WithMany().HasForeignKey(l => l.StockBatchId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(l => l.Material).WithMany().HasForeignKey(l => l.MaterialId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.PhysicalMaterial).WithMany().HasForeignKey(l => l.PhysicalMaterialId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.PalletPosition).WithMany().HasForeignKey(l => l.PalletPositionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.StockSubtype).WithMany().HasForeignKey(l => l.StockSubtypeId).OnDelete(DeleteBehavior.Restrict);
    }
}
