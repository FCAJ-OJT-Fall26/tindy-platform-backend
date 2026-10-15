using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class ProfessionalRoleConfiguration
    : IEntityTypeConfiguration<ProfessionalRole>
{
    public void Configure(EntityTypeBuilder<ProfessionalRole> builder)
    {
        builder.ToTable("professional_roles");

        builder.HasKey(x => x.ProfessionalRoleId);

        builder.Property(x => x.ProfessionalRoleId)
            .HasColumnName("professional_role_id")
            .ValueGeneratedNever();

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.NameNormalized)
            .HasColumnName("name_normalized")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasColumnName("description")
            .HasMaxLength(500);

        builder.Property(x => x.DisplayOrder)
            .HasColumnName("display_order")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.HasIndex(x => x.NameNormalized)
            .IsUnique()
            .HasDatabaseName("ux_professional_roles_name_normalized");
    }
}
