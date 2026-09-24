using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public class SeasonMonthMapConfiguration : IEntityTypeConfiguration<SeasonMonthMap>
{
    public void Configure(EntityTypeBuilder<SeasonMonthMap> builder)
    {
        builder.ToTable("SeasonMonthMaps");

        builder.HasIndex(s => s.Month).IsUnique();

        builder.Property(s => s.CreatedByUserId).HasMaxLength(450);
        builder.Property(s => s.UpdatedByUserId).HasMaxLength(450);

        builder.Property(s => s.SeasonId).HasColumnName("Season");
        builder.HasOne(s => s.Season)
            .WithMany()
            .HasForeignKey(s => s.SeasonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
