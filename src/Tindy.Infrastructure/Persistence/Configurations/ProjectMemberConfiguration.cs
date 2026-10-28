using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("project_members");

        builder.HasKey(x => new { x.ProjectId, x.UserId });
        builder.Property(x => x.ProjectId).HasColumnName("project_id");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.RoleTitle).HasColumnName("role_title").HasMaxLength(100).IsRequired();
        builder.Property(x => x.IsLeader).HasColumnName("is_leader").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.JoinedAt).HasColumnName("joined_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(50).HasDefaultValue("active").IsRequired();

        builder.HasOne(x => x.Project).WithMany(x => x.Members)
            .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User).WithMany(x => x.ProjectMemberships)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.UserId).HasDatabaseName("ix_project_members_user_id");
    }
}
