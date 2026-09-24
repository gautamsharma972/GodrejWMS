using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public class DesignTypeConfiguration : IEntityTypeConfiguration<DesignType>
{
    public void Configure(EntityTypeBuilder<DesignType> builder)
    {
        builder.ToTable("DesignTypes");

        builder.HasIndex(d => d.Code).IsUnique();
        builder.Property(d => d.Code).HasMaxLength(20).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(200);

        builder.Property(d => d.CreatedByUserId).HasMaxLength(450);
        builder.Property(d => d.UpdatedByUserId).HasMaxLength(450);
    }
}
