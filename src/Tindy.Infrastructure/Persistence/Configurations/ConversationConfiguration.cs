using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");
        builder.Property(x => x.ProjectId).HasColumnName("project_id");
        builder.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.ProjectId).IsUnique();
        builder.HasKey(x => x.ConversationId);
        builder.Property(x => x.ConsentSource).HasColumnName("consent_source").HasMaxLength(20);
        builder.Property(x => x.DirectKey).HasColumnName("direct_key").HasMaxLength(65);
        builder.HasIndex(x => x.DirectKey).IsUnique().HasFilter("type = 'direct' AND status = 'active'");
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
        builder.Property(x => x.Type).HasColumnName("type").HasMaxLength(30).HasDefaultValue("direct").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(30).HasDefaultValue("active").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.LastMessageAt).HasColumnName("last_message_at");
        builder.HasIndex(x => x.LastMessageAt).HasDatabaseName("ix_conversations_last_message_at");
    }
}
