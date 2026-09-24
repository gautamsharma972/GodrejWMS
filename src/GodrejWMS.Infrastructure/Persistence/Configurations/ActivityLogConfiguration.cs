using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> builder)
    {
        builder.ToTable("ActivityLogs");

        builder.Property(a => a.UserId).HasMaxLength(450);
        builder.Property(a => a.UserName).HasMaxLength(256);
        builder.Property(a => a.Action).HasMaxLength(40).IsRequired();
        builder.Property(a => a.EntityName).HasMaxLength(120).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(120);
        builder.Property(a => a.Summary).HasMaxLength(500).IsRequired();
        builder.Property(a => a.OldValuesJson).HasColumnType("longtext");
        builder.Property(a => a.NewValuesJson).HasColumnType("longtext");

        builder.HasIndex(a => a.OccurredAt);
        builder.HasIndex(a => a.Action);
        builder.HasIndex(a => a.EntityName);
        builder.HasIndex(a => a.UserName);
    }
}
