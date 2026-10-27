using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public class RegionConfiguration : IEntityTypeConfiguration<Region>
{
    public void Configure(EntityTypeBuilder<Region> builder)
    {
        builder.ToTable("Regions");
        builder.HasIndex(r => r.Code).IsUnique();
        builder.Property(r => r.Code).HasMaxLength(20).IsRequired();
        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(300);
        builder.Property(r => r.CreatedByUserId).HasMaxLength(450);
        builder.Property(r => r.UpdatedByUserId).HasMaxLength(450);

    }
}

public class StateConfiguration : IEntityTypeConfiguration<State>
{
    public void Configure(EntityTypeBuilder<State> builder)
    {
        builder.ToTable("States");
        builder.HasIndex(s => s.Code).IsUnique();
        builder.Property(s => s.Code).HasMaxLength(20).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.CreatedByUserId).HasMaxLength(450);
        builder.Property(s => s.UpdatedByUserId).HasMaxLength(450);

        builder.HasOne(s => s.Region)
            .WithMany(r => r.States)
            .HasForeignKey(s => s.RegionId)
            .OnDelete(DeleteBehavior.Restrict);

    }
}

public class CityConfiguration : IEntityTypeConfiguration<City>
{
    public void Configure(EntityTypeBuilder<City> builder)
    {
        builder.ToTable("Cities");
        builder.HasIndex(c => c.Code).IsUnique();
        builder.Property(c => c.Code).HasMaxLength(20).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.CreatedByUserId).HasMaxLength(450);
        builder.Property(c => c.UpdatedByUserId).HasMaxLength(450);

        builder.HasOne(c => c.State)
            .WithMany(s => s.Cities)
            .HasForeignKey(c => c.StateId)
            .OnDelete(DeleteBehavior.Restrict);

    }
}
