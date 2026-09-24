using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public class RackConfiguration : IEntityTypeConfiguration<Rack>
{
    public void Configure(EntityTypeBuilder<Rack> builder)
    {
        builder.ToTable("Racks");

        builder.HasIndex(r => r.Code).IsUnique();
        builder.HasIndex(r => new { r.WarehouseId, r.Code }).IsUnique();
        builder.HasOne(r => r.Warehouse).WithMany().HasForeignKey(r => r.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.Code).HasMaxLength(10).IsRequired();
        builder.Property(r => r.Name).HasMaxLength(100);

        builder.Property(r => r.ShelfLengthMm).HasPrecision(12, 3);
        builder.Property(r => r.ShelfWidthMm).HasPrecision(12, 3);
        builder.Property(r => r.ShelfHeightMm).HasPrecision(12, 3);
        builder.Property(r => r.StartingDistancePriority).HasDefaultValue(1);

        builder.Property(r => r.CreatedByUserId).HasMaxLength(450);
        builder.Property(r => r.UpdatedByUserId).HasMaxLength(450);

        builder.HasMany(r => r.PalletPositions)
            .WithOne(p => p.Rack)
            .HasForeignKey(p => p.RackId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PalletPositionConfiguration : IEntityTypeConfiguration<PalletPosition>
{
    public void Configure(EntityTypeBuilder<PalletPosition> builder)
    {
        builder.ToTable("PalletPositions");

        builder.HasIndex(p => p.LocationCode).IsUnique();
        builder.HasIndex(p => new { p.RackId, p.Column, p.Level }).IsUnique();

        builder.Property(p => p.LocationCode).HasMaxLength(20).IsRequired();
        builder.Property(p => p.FlatLabel).HasMaxLength(20);
        builder.Property(p => p.DistancePriority).HasDefaultValue(100);
        builder.Property(p => p.MaxPallets).HasDefaultValue(2);
        builder.Property(p => p.BoxesPerPallet).HasDefaultValue(40);
        builder.Property(p => p.CapacityBoxes).HasDefaultValue(80);

        builder.Property(p => p.CreatedByUserId).HasMaxLength(450);
        builder.Property(p => p.UpdatedByUserId).HasMaxLength(450);

        builder.Property(p => p.RowVersion).IsConcurrencyToken();

        // Reuses the pre-existing "LocationSubtype" column (previously an enum int) as the new
        // FK column - the stored values (1=Good, 2=Damage, 3=Expire, 4=Hold) already match the
        // seeded LocationSubtype master's ids, so no data migration is needed.
        builder.Property(p => p.LocationSubtypeId).HasColumnName("LocationSubtype");
        builder.HasOne(p => p.LocationSubtype)
            .WithMany()
            .HasForeignKey(p => p.LocationSubtypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Reuses the pre-existing "ZoneType" column (previously an enum int) as the new FK column -
        // the stored values (1=Fast, 2=Reserve, 3=Seasonal, 4=DispatchNear) already match the
        // seeded ZoneType master's ids, so no data migration is needed.
        builder.Property(p => p.ZoneTypeId).HasColumnName("ZoneType");
        builder.HasOne(p => p.ZoneType)
            .WithMany()
            .HasForeignKey(p => p.ZoneTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Reuses the pre-existing "LocationType" column (previously an enum int) as the new FK
        // column - the stored values (1=Rack, 2=Pallet, 3=Floor, 4=Yard) already match the seeded
        // LocationType master's ids, so no data migration is needed.
        builder.Property(p => p.LocationTypeId).HasColumnName("LocationType");
        builder.HasOne(p => p.LocationType)
            .WithMany()
            .HasForeignKey(p => p.LocationTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
