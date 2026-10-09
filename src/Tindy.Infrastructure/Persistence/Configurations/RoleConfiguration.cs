using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.HasKey(x => x.RoleId);

        builder.Property(x => x.RoleId)
            .HasColumnName("role_id");

        builder.Property(x => x.RoleName)
            .HasColumnName("role_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(x => x.RoleName)
            .IsUnique()
            .HasDatabaseName("ux_roles_role_name");
    }
}