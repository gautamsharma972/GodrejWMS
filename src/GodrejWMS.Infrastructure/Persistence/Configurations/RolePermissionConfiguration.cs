using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GodrejWMS.Infrastructure.Persistence.Configurations;

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");
        builder.HasKey(p => new { p.RoleName, p.PermissionKey });
        builder.Property(p => p.RoleName).HasMaxLength(50);
        builder.Property(p => p.PermissionKey).HasMaxLength(100);
    }
}
