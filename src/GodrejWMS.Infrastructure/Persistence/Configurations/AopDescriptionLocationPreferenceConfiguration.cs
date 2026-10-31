using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public sealed class AopDescriptionLocationPreferenceConfiguration : IEntityTypeConfiguration<AopDescriptionLocationPreference>
{
    public void Configure(EntityTypeBuilder<AopDescriptionLocationPreference> builder)
    {
        builder.ToTable("AopDescriptionLocationPreferences");
        // Exact-duplicate guard at the database level; range overlap is enforced in the command
        // validation (AopDescriptionLocationPreferenceRules), since it cannot be expressed as an index.
        builder.HasIndex(x => new { x.AopDescriptionId, x.FromPalletPositionId, x.ToPalletPositionId }).IsUnique();
        builder.Property(x => x.CreatedByUserId).HasMaxLength(450);
        builder.Property(x => x.UpdatedByUserId).HasMaxLength(450);

        builder.HasOne(x => x.AopDescription)
            .WithMany(x => x.LocationPreferences)
            .HasForeignKey(x => x.AopDescriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Deprecated Brand link, kept nullable for rollback (see the entity). No navigation on either side.
        builder.HasOne<Brand>()
            .WithMany()
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
