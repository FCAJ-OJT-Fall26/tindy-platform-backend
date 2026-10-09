using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public class PermissionConfiguration
    : IEntityTypeConfiguration<Permission>
{
    public void Configure(
        EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");

        builder.HasKey(x => x.PermissionId);

        builder.Property(x => x.PermissionId)
            .HasColumnName("permission_id");

        builder.Property(x => x.PermissionName)
            .HasColumnName("permission_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.HasIndex(x => x.PermissionName)
            .IsUnique()
            .HasDatabaseName("ux_permissions_permission_name");
    }
}