using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("Warehouses");
        builder.HasKey(w => w.Id);
        builder.HasIndex(w => w.Code).IsUnique();
        builder.Property(w => w.Code).HasMaxLength(32).IsRequired();
        builder.Property(w => w.Name).HasMaxLength(160).IsRequired();
        builder.Property(w => w.Address).HasMaxLength(500);
        builder.Property(w => w.AllowPkmMixing).HasDefaultValue(true);

        builder.HasOne(w => w.City)
            .WithMany()
            .HasForeignKey(w => w.CityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(w => w.WarehouseCategory)
            .WithMany()
            .HasForeignKey(w => w.WarehouseCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

    }
}

public sealed class UserWarehouseConfiguration : IEntityTypeConfiguration<UserWarehouse>
{
    public void Configure(EntityTypeBuilder<UserWarehouse> builder)
    {
        builder.ToTable("UserWarehouses");
        builder.HasKey(w => new { w.UserId, w.WarehouseId });
        builder.Property(w => w.UserId).HasMaxLength(450);
        builder.HasOne(w => w.Warehouse).WithMany().HasForeignKey(w => w.WarehouseId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class UserRegionConfiguration : IEntityTypeConfiguration<UserRegion>
{
    public void Configure(EntityTypeBuilder<UserRegion> builder)
    {
        builder.ToTable("UserRegions");
        builder.HasKey(r => new { r.UserId, r.RegionId });
        builder.Property(r => r.UserId).HasMaxLength(450);
        builder.HasOne(r => r.Region).WithMany().HasForeignKey(r => r.RegionId).OnDelete(DeleteBehavior.Cascade);
    }
}
