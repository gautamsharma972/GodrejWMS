using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public class StockLedgerEntryConfiguration : IEntityTypeConfiguration<StockLedgerEntry>
{
    public void Configure(EntityTypeBuilder<StockLedgerEntry> builder)
    {
        builder.ToTable("StockLedgerEntries");

        builder.Property(e => e.PostingNumber).HasMaxLength(60).IsRequired();
        builder.Property(e => e.EntryType).HasMaxLength(30).IsRequired();
        builder.Property(e => e.QuantityBoxes).HasPrecision(14, 3);
        builder.Property(e => e.BalanceAfterBoxes).HasPrecision(14, 3);
        builder.Property(e => e.ReferenceType).HasMaxLength(30);
        builder.Property(e => e.ReferenceNumber).HasMaxLength(60);
        builder.Property(e => e.Notes).HasMaxLength(300);
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.CreatedByUserName).HasMaxLength(256);

        builder.HasIndex(e => e.PostingNumber);
        builder.HasIndex(e => new { e.PalletPositionId, e.CreatedAt });
        builder.HasIndex(e => new { e.MaterialId, e.CreatedAt });
        builder.HasIndex(e => new { e.ReferenceType, e.ReferenceNumber });
        builder.HasIndex(e => new { e.WarehouseId, e.CreatedAt });

        builder.HasOne(e => e.Material).WithMany().HasForeignKey(e => e.MaterialId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.PalletPosition).WithMany().HasForeignKey(e => e.PalletPositionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class VirtualLocationSequenceConfiguration : IEntityTypeConfiguration<VirtualLocationSequence>
{
    public void Configure(EntityTypeBuilder<VirtualLocationSequence> builder)
    {
        builder.ToTable("VirtualLocationSequences");
        builder.HasKey(s => s.WarehouseId);
        builder.Property(s => s.WarehouseId).ValueGeneratedNever();
    }
}

public class VirtualLocationLogConfiguration : IEntityTypeConfiguration<VirtualLocationLog>
{
    public void Configure(EntityTypeBuilder<VirtualLocationLog> builder)
    {
        builder.ToTable("VirtualLocationLogs");
        builder.HasIndex(l => new { l.PalletPositionId, l.ReleasedAt });
        builder.HasIndex(l => l.InwardTransactionId);
        builder.HasOne(l => l.PalletPosition).WithMany().HasForeignKey(l => l.PalletPositionId).OnDelete(DeleteBehavior.Restrict);
    }
}
