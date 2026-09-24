using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");

        builder.HasIndex(m => m.MovementNumber).IsUnique();
        builder.Property(m => m.MovementNumber).HasMaxLength(40).IsRequired();
        builder.Property(m => m.MovementType).HasMaxLength(40).IsRequired();
        builder.Property(m => m.QuantityBoxes).HasPrecision(14, 3);
        builder.Property(m => m.PerformedByUserId).HasMaxLength(450);
        builder.Property(m => m.PerformedByUserName).HasMaxLength(256);
        builder.Property(m => m.CreatedByUserId).HasMaxLength(450);
        builder.Property(m => m.UpdatedByUserId).HasMaxLength(450);

        builder.HasIndex(m => m.MaterialId);
        builder.HasIndex(m => m.CreatedAt);

        builder.HasOne(m => m.Material)
            .WithMany()
            .HasForeignKey(m => m.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.StockSubtype)
            .WithMany()
            .HasForeignKey(m => m.StockSubtypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Two FKs to the same PalletPositions table - both Restrict, and each needs its own
        // named relationship so EF doesn't try to collapse them into a single navigation pair.
        builder.HasOne(m => m.SourcePalletPosition)
            .WithMany()
            .HasForeignKey(m => m.SourcePalletPositionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.DestinationPalletPosition)
            .WithMany()
            .HasForeignKey(m => m.DestinationPalletPositionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.Reason)
            .WithMany()
            .HasForeignKey(m => m.ReasonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
