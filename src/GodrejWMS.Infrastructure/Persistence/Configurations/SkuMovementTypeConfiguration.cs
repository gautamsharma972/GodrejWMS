using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public class SkuMovementTypeConfiguration : IEntityTypeConfiguration<SkuMovementType>
{
    public void Configure(EntityTypeBuilder<SkuMovementType> builder)
    {
        builder.ToTable("SkuMovementTypes");

        builder.HasIndex(s => s.Code).IsUnique();

        builder.Property(s => s.Code).HasMaxLength(20).IsRequired();
        builder.Property(s => s.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(300);

        builder.Property(s => s.CreatedByUserId).HasMaxLength(450);
        builder.Property(s => s.UpdatedByUserId).HasMaxLength(450);
    }
}
