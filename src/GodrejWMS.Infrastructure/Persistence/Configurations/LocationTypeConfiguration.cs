using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public class LocationTypeConfiguration : IEntityTypeConfiguration<LocationType>
{
    public void Configure(EntityTypeBuilder<LocationType> builder)
    {
        builder.ToTable("LocationTypes");

        builder.HasIndex(l => l.Code).IsUnique();
        builder.Property(l => l.Code).HasMaxLength(20).IsRequired();
        builder.Property(l => l.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(l => l.Description).HasMaxLength(300);

        builder.Property(l => l.CreatedByUserId).HasMaxLength(450);
        builder.Property(l => l.UpdatedByUserId).HasMaxLength(450);
    }
}
