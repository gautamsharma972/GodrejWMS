using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public sealed class MaterialConversionConfiguration : IEntityTypeConfiguration<MaterialConversion>
{
    public void Configure(EntityTypeBuilder<MaterialConversion> builder)
    {
        builder.ToTable("MaterialConversions");
        builder.HasIndex(c => c.ConversionNumber).IsUnique();
        builder.HasIndex(c => new { c.WarehouseId, c.Status, c.RequestedAt });
        builder.HasIndex(c => new { c.FromMaterialId, c.ToMaterialId });
        builder.Property(c => c.ConversionNumber).HasMaxLength(40).IsRequired();
        builder.Property(c => c.TotalBoxes).HasPrecision(14, 3);
        builder.Property(c => c.ReasonText).HasMaxLength(500);
        builder.Property(c => c.RequestedByUserId).HasMaxLength(450);
        builder.Property(c => c.RequestedByUserName).HasMaxLength(256);
        builder.Property(c => c.ReviewedByUserId).HasMaxLength(450);
        builder.Property(c => c.ReviewedByUserName).HasMaxLength(256);
        builder.Property(c => c.ReviewComment).HasMaxLength(500);
        builder.Property(c => c.CreatedByUserId).HasMaxLength(450);
        builder.Property(c => c.UpdatedByUserId).HasMaxLength(450);
        builder.Property(c => c.RowVersion).IsConcurrencyToken();

        builder.HasOne(c => c.Warehouse).WithMany().HasForeignKey(c => c.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.FromMaterial).WithMany().HasForeignKey(c => c.FromMaterialId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.ToMaterial).WithMany().HasForeignKey(c => c.ToMaterialId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(c => c.Lines).WithOne(l => l.MaterialConversion).HasForeignKey(l => l.MaterialConversionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MaterialConversionLineConfiguration : IEntityTypeConfiguration<MaterialConversionLine>
{
    public void Configure(EntityTypeBuilder<MaterialConversionLine> builder)
    {
        builder.ToTable("MaterialConversionLines");
        builder.HasIndex(l => l.StockBatchId);
        builder.Property(l => l.QuantityBoxes).HasPrecision(14, 3);
        builder.Property(l => l.SystemQuantityAtRequest).HasPrecision(14, 3);
        builder.Property(l => l.CreatedByUserId).HasMaxLength(450);
        builder.Property(l => l.UpdatedByUserId).HasMaxLength(450);

        builder.HasOne(l => l.StockBatch).WithMany().HasForeignKey(l => l.StockBatchId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(l => l.PalletPosition).WithMany().HasForeignKey(l => l.PalletPositionId).OnDelete(DeleteBehavior.Restrict);
    }
}
