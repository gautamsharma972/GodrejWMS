using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public class ZoneTypeConfiguration : IEntityTypeConfiguration<ZoneType>
{
    public void Configure(EntityTypeBuilder<ZoneType> builder)
    {
        builder.ToTable("ZoneTypes");

        builder.HasIndex(z => z.Code).IsUnique();
        builder.Property(z => z.Code).HasMaxLength(20).IsRequired();
        builder.Property(z => z.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(z => z.Description).HasMaxLength(300);

        builder.Property(z => z.CreatedByUserId).HasMaxLength(450);
        builder.Property(z => z.UpdatedByUserId).HasMaxLength(450);
    }
}
