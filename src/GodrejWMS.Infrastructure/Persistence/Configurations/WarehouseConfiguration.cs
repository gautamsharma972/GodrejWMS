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
        builder.HasData(new Warehouse { Id = 1, Code = "GCPL", Name = "GCPL", IsActive = true });
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
