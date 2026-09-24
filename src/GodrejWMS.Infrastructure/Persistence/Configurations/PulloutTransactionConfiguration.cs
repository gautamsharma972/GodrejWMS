using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public class PulloutTransactionConfiguration : IEntityTypeConfiguration<PulloutTransaction>
{
    public void Configure(EntityTypeBuilder<PulloutTransaction> builder)
    {
        builder.ToTable("PulloutTransactions");
        builder.Property(t => t.RowVersion).IsConcurrencyToken();

        builder.HasIndex(t => t.ReferenceNumber).IsUnique();
        builder.HasIndex(t => new { t.WarehouseId, t.CreatedAt });
        builder.HasOne(t => t.Warehouse).WithMany().HasForeignKey(t => t.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(t => t.ReferenceNumber).HasMaxLength(40).IsRequired();
        builder.Property(t => t.RejectedByUserId).HasMaxLength(450);
        builder.Property(t => t.RejectedByUserName).HasMaxLength(256);
        builder.Property(t => t.RejectionReason).HasMaxLength(500);
        builder.Property(t => t.CreatedByUserId).HasMaxLength(450);
        builder.Property(t => t.UpdatedByUserId).HasMaxLength(450);

        builder.HasMany(t => t.Lines)
            .WithOne(l => l.PulloutTransaction)
            .HasForeignKey(l => l.PulloutTransactionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PulloutTransactionLineConfiguration : IEntityTypeConfiguration<PulloutTransactionLine>
{
    public void Configure(EntityTypeBuilder<PulloutTransactionLine> builder)
    {
        builder.ToTable("PulloutTransactionLines");

        builder.Property(l => l.RequestedQuantityBoxes).HasPrecision(14, 3);
        builder.Property(l => l.PickedQuantityBoxes).HasPrecision(14, 3);
        builder.Property(l => l.Remarks).HasMaxLength(500);

        builder.HasOne(l => l.Material)
            .WithMany()
            .HasForeignKey(l => l.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(l => l.Picks)
            .WithOne(p => p.PulloutTransactionLine)
            .HasForeignKey(p => p.PulloutTransactionLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PulloutPickConfiguration : IEntityTypeConfiguration<PulloutPick>
{
    public void Configure(EntityTypeBuilder<PulloutPick> builder)
    {
        builder.ToTable("PulloutPicks");

        builder.Property(p => p.QuantityBoxes).HasPrecision(14, 3);

        builder.HasOne(p => p.PalletPosition)
            .WithMany()
            .HasForeignKey(p => p.PalletPositionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
