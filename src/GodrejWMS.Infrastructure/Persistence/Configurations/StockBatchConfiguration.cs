using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public class StockBatchConfiguration : IEntityTypeConfiguration<StockBatch>
{
    public void Configure(EntityTypeBuilder<StockBatch> builder)
    {
        builder.ToTable("StockBatches");

        builder.Property(b => b.QuantityBoxes).HasPrecision(14, 3);

        // MySQL has no native rowversion column, so RowVersion is a plain counter that the
        // allocation services increment on every mutation; marking it a concurrency token still
        // makes EF Core include it in the UPDATE's WHERE clause and throw
        // DbUpdateConcurrencyException if another request changed the batch first.
        builder.Property(b => b.RowVersion).IsConcurrencyToken();

        builder.Property(b => b.CreatedByUserId).HasMaxLength(450);
        builder.Property(b => b.UpdatedByUserId).HasMaxLength(450);

        builder.HasIndex(b => b.MaterialId);
        builder.HasIndex(b => new { b.MaterialId, b.MfgMonth, b.PalletPositionId }).IsUnique();

        builder.HasOne(b => b.Material)
            .WithMany(m => m.StockBatches)
            .HasForeignKey(b => b.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.PalletPosition)
            .WithMany(p => p.StockBatches)
            .HasForeignKey(b => b.PalletPositionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Reuses the pre-existing "StockSubtype" column (previously an enum int) as the new FK
        // column - stored values already match the seeded LocationSubtype master's ids.
        builder.Property(b => b.StockSubtypeId).HasColumnName("StockSubtype");
        builder.HasOne(b => b.StockSubtype)
            .WithMany()
            .HasForeignKey(b => b.StockSubtypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
