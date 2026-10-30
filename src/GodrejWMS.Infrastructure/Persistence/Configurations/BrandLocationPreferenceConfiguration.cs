using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public sealed class BrandLocationPreferenceConfiguration : IEntityTypeConfiguration<BrandLocationPreference>
{
    public void Configure(EntityTypeBuilder<BrandLocationPreference> builder)
    {
        builder.ToTable("BrandLocationPreferences");
        builder.HasIndex(x => new { x.BrandId, x.FromPalletPositionId, x.ToPalletPositionId }).IsUnique();
        builder.Property(x => x.CreatedByUserId).HasMaxLength(450);
        builder.Property(x => x.UpdatedByUserId).HasMaxLength(450);

        builder.HasOne(x => x.Brand)
            .WithMany(x => x.LocationPreferences)
            .HasForeignKey(x => x.BrandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.FromPalletPosition)
            .WithMany()
            .HasForeignKey(x => x.FromPalletPositionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ToPalletPosition)
            .WithMany()
            .HasForeignKey(x => x.ToPalletPositionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
