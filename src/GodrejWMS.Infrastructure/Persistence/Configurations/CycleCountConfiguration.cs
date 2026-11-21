using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public sealed class CycleCountBatchConfiguration : IEntityTypeConfiguration<CycleCountBatch>
{
    public void Configure(EntityTypeBuilder<CycleCountBatch> builder)
    {
        builder.ToTable("CycleCountBatches");
        builder.HasIndex(b => b.BatchNumber).IsUnique();
        builder.HasIndex(b => new { b.WarehouseId, b.Status });
        builder.Property(b => b.BatchNumber).HasMaxLength(40).IsRequired();
        builder.Property(b => b.Remarks).HasMaxLength(500);
        builder.Property(b => b.DropReason).HasMaxLength(500);
        builder.Property(b => b.CreatedByUserId).HasMaxLength(450);
        builder.Property(b => b.CreatedByUserName).HasMaxLength(256);
        builder.Property(b => b.UpdatedByUserId).HasMaxLength(450);
        builder.Property(b => b.UpdatedByUserName).HasMaxLength(256);
        builder.Property(b => b.RowVersion).IsConcurrencyToken();
        builder.HasOne(b => b.Warehouse).WithMany().HasForeignKey(b => b.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(b => b.Racks).WithOne(r => r.CycleCountBatch).HasForeignKey(r => r.CycleCountBatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(b => b.Cards).WithOne(c => c.CycleCountBatch).HasForeignKey(c => c.CycleCountBatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(b => b.History).WithOne(h => h.CycleCountBatch).HasForeignKey(h => h.CycleCountBatchId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CycleCountBatchRackConfiguration : IEntityTypeConfiguration<CycleCountBatchRack>
{
    public void Configure(EntityTypeBuilder<CycleCountBatchRack> builder)
    {
        builder.ToTable("CycleCountBatchRacks");
        builder.HasIndex(r => new { r.CycleCountBatchId, r.RackCode }).IsUnique();
        builder.Property(r => r.RackCode).HasMaxLength(40).IsRequired();
        builder.Property(r => r.SubmittedByUserId).HasMaxLength(450);
        builder.Property(r => r.SubmittedByUserName).HasMaxLength(256);
        builder.Property(r => r.CreatedByUserId).HasMaxLength(450);
        builder.Property(r => r.UpdatedByUserId).HasMaxLength(450);
        builder.Property(r => r.RowVersion).IsConcurrencyToken();
    }
}

public sealed class CycleCountCardConfiguration : IEntityTypeConfiguration<CycleCountCard>
{
    public void Configure(EntityTypeBuilder<CycleCountCard> builder)
    {
        builder.ToTable("CycleCountCards");
        builder.HasIndex(c => new { c.CycleCountBatchId, c.RackCode, c.SequenceKey });
        builder.HasIndex(c => new { c.CycleCountBatchId, c.PalletPositionId });
        builder.Property(c => c.RackCode).HasMaxLength(40).IsRequired();
        builder.Property(c => c.SequenceKey).HasMaxLength(120).IsRequired();
        builder.Property(c => c.SystemQuantityBoxes).HasPrecision(14, 3);
        builder.Property(c => c.PhysicalQuantityBoxes).HasPrecision(14, 3);
        builder.Property(c => c.DifferenceBoxes).HasPrecision(14, 3);
        builder.Property(c => c.CountedByUserId).HasMaxLength(450);
        builder.Property(c => c.CountedByUserName).HasMaxLength(256);
        builder.Property(c => c.SubmittedByUserId).HasMaxLength(450);
        builder.Property(c => c.SubmittedByUserName).HasMaxLength(256);
        builder.Property(c => c.CreatedByUserId).HasMaxLength(450);
        builder.Property(c => c.UpdatedByUserId).HasMaxLength(450);
        builder.Property(c => c.RowVersion).IsConcurrencyToken();
        builder.HasOne(c => c.PalletPosition).WithMany().HasForeignKey(c => c.PalletPositionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.SystemMaterial).WithMany().HasForeignKey(c => c.SystemMaterialId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.PhysicalMaterial).WithMany().HasForeignKey(c => c.PhysicalMaterialId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CycleCountHistoryConfiguration : IEntityTypeConfiguration<CycleCountHistory>
{
    public void Configure(EntityTypeBuilder<CycleCountHistory> builder)
    {
        builder.ToTable("CycleCountHistory");
        builder.HasIndex(h => new { h.CycleCountBatchId, h.OccurredAt });
        builder.HasIndex(h => h.CycleCountCardId);
        builder.Property(h => h.RackCode).HasMaxLength(40);
        builder.Property(h => h.Action).HasMaxLength(40).IsRequired();
        builder.Property(h => h.PreviousValue).HasMaxLength(1000);
        builder.Property(h => h.NewValue).HasMaxLength(1000);
        builder.Property(h => h.Reason).HasMaxLength(500);
        builder.Property(h => h.UserId).HasMaxLength(450);
        builder.Property(h => h.UserName).HasMaxLength(256);
    }
}

public sealed class CycleCountReviewConfiguration : IEntityTypeConfiguration<CycleCountReview>
{
    public void Configure(EntityTypeBuilder<CycleCountReview> builder)
    {
        builder.ToTable("CycleCountReviews");
        builder.HasIndex(r => new { r.CycleCountBatchId, r.RackCode }).IsUnique();
        builder.Property(r => r.RackCode).HasMaxLength(40).IsRequired();
        builder.Property(r => r.ReviewedByUserId).HasMaxLength(450);
        builder.Property(r => r.ReviewedByUserName).HasMaxLength(256);
        builder.Property(r => r.ReviewRemarks).HasMaxLength(1000);
        builder.Property(r => r.CreatedByUserId).HasMaxLength(450);
        builder.Property(r => r.UpdatedByUserId).HasMaxLength(450);
        builder.HasOne(r => r.CycleCountBatch).WithMany().HasForeignKey(r => r.CycleCountBatchId).OnDelete(DeleteBehavior.Cascade);
    }
}
