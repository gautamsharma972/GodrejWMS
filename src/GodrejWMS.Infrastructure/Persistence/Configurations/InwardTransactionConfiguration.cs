using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public class InwardTransactionConfiguration : IEntityTypeConfiguration<InwardTransaction>
{
    public void Configure(EntityTypeBuilder<InwardTransaction> builder)
    {
        builder.ToTable("InwardTransactions");

        builder.HasIndex(t => t.ReferenceNumber).IsUnique();
        builder.HasIndex(t => new { t.WarehouseId, t.CreatedAt });
        builder.HasOne(t => t.Warehouse).WithMany().HasForeignKey(t => t.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(t => t.ReferenceNumber).HasMaxLength(40).IsRequired();
        builder.Property(t => t.CreatedByUserId).HasMaxLength(450);
        builder.Property(t => t.UpdatedByUserId).HasMaxLength(450);
        builder.Property(t => t.RejectedByUserId).HasMaxLength(450);
        builder.Property(t => t.RejectedByUserName).HasMaxLength(256);
        builder.Property(t => t.RejectionReason).HasMaxLength(500);

        builder.HasMany(t => t.Lines)
            .WithOne(l => l.InwardTransaction)
            .HasForeignKey(l => l.InwardTransactionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class InwardTransactionLineConfiguration : IEntityTypeConfiguration<InwardTransactionLine>
{
    public void Configure(EntityTypeBuilder<InwardTransactionLine> builder)
    {
        builder.ToTable("InwardTransactionLines");

        builder.Property(l => l.RequestedQuantityBoxes).HasPrecision(14, 3);
        builder.Property(l => l.AllocatedQuantityBoxes).HasPrecision(14, 3);
        builder.Property(l => l.Remarks).HasMaxLength(500);

        builder.HasOne(l => l.Material)
            .WithMany()
            .HasForeignKey(l => l.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(l => l.Putaways)
            .WithOne(p => p.InwardTransactionLine)
            .HasForeignKey(p => p.InwardTransactionLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class InwardPutawayConfiguration : IEntityTypeConfiguration<InwardPutaway>
{
    public void Configure(EntityTypeBuilder<InwardPutaway> builder)
    {
        builder.ToTable("InwardPutaways");

        builder.Property(p => p.QuantityBoxes).HasPrecision(14, 3);
        builder.Property(p => p.AllocationReason).HasMaxLength(250);
        builder.Property(p => p.OverrideReason).HasMaxLength(250);
        builder.Property(p => p.ConfirmedByUserId).HasMaxLength(450);
        builder.Property(p => p.ConfirmedByUserName).HasMaxLength(256);
        builder.Property(p => p.RowVersion).IsConcurrencyToken();
        builder.HasIndex(p => new { p.PalletPositionId, p.IsConfirmed });

        builder.HasOne(p => p.PalletPosition)
            .WithMany()
            .HasForeignKey(p => p.PalletPositionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
