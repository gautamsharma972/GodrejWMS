using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.ToTable("Materials");

        builder.HasIndex(m => m.MaterialNumber).IsUnique();

        builder.Property(m => m.Description).HasMaxLength(200).IsRequired();
        builder.Property(m => m.DesignType).HasMaxLength(20).IsRequired();
        builder.Property(m => m.CharacteristicValue).HasMaxLength(20);

        builder.Property(m => m.MrpPrice).HasPrecision(12, 2);
        builder.Property(m => m.LengthMm).HasPrecision(12, 3);
        builder.Property(m => m.WidthMm).HasPrecision(12, 3);
        builder.Property(m => m.HeightMm).HasPrecision(12, 3);
        builder.Property(m => m.VolumeMm3).HasPrecision(18, 3);
        builder.Property(m => m.NetWeightKg).HasPrecision(12, 4);
        builder.Property(m => m.GrossWeightKg).HasPrecision(12, 4);
        builder.Property(m => m.BoxWeightKg).HasPrecision(12, 4);
        builder.Property(m => m.PalletWeightKg).HasPrecision(14, 4);

        builder.Property(m => m.CreatedByUserId).HasMaxLength(450);
        builder.Property(m => m.UpdatedByUserId).HasMaxLength(450);

        builder.Property(m => m.MovementTypeId).HasColumnName("MovementType");
        builder.HasOne(m => m.MovementType)
            .WithMany()
            .HasForeignKey(m => m.MovementTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(m => m.SeasonId).HasColumnName("Season");
        builder.HasOne(m => m.Season)
            .WithMany()
            .HasForeignKey(m => m.SeasonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.PreferredZoneType)
            .WithMany()
            .HasForeignKey(m => m.PreferredZoneTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
