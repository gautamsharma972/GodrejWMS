using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public class MovementReasonConfiguration : IEntityTypeConfiguration<MovementReason>
{
    public void Configure(EntityTypeBuilder<MovementReason> builder)
    {
        builder.ToTable("MovementReasons");

        builder.HasIndex(r => r.Code).IsUnique();
        builder.Property(r => r.Code).HasMaxLength(40).IsRequired();
        builder.Property(r => r.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(300);

        builder.Property(r => r.CreatedByUserId).HasMaxLength(450);
        builder.Property(r => r.UpdatedByUserId).HasMaxLength(450);
    }
}
