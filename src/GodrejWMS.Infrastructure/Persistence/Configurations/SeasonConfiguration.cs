using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> builder)
    {
        builder.ToTable("Seasons");

        // The retired Season enum stored Rainy=0, and 0 has special "auto-generate" behavior on a
        // MySQL AUTO_INCREMENT column. Ids are assigned explicitly by the fixed seed instead, so
        // Rainy=0/Summer=1/Winter=2 can be reused verbatim with zero data rewriting.
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.HasIndex(s => s.Code).IsUnique();
        builder.Property(s => s.Code).HasMaxLength(20).IsRequired();
        builder.Property(s => s.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(300);

        builder.Property(s => s.CreatedByUserId).HasMaxLength(450);
        builder.Property(s => s.UpdatedByUserId).HasMaxLength(450);
    }
}
