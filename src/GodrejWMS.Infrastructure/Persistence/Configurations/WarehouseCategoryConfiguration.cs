using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public class WarehouseCategoryConfiguration : IEntityTypeConfiguration<WarehouseCategory>
{
    public void Configure(EntityTypeBuilder<WarehouseCategory> builder)
    {
        builder.ToTable("WarehouseCategories");
        builder.HasIndex(w => w.Code).IsUnique();
        builder.Property(w => w.Code).HasMaxLength(20).IsRequired();
        builder.Property(w => w.Name).HasMaxLength(100).IsRequired();
        builder.Property(w => w.Description).HasMaxLength(300);
        builder.Property(w => w.CreatedByUserId).HasMaxLength(450);
        builder.Property(w => w.UpdatedByUserId).HasMaxLength(450);

    }
}
