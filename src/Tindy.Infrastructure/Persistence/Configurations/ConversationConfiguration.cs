using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");
        builder.HasKey(x => x.ConversationId);
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
        builder.Property(x => x.Type).HasColumnName("type").HasMaxLength(30).HasDefaultValue("direct").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(30).HasDefaultValue("active").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.LastMessageAt).HasColumnName("last_message_at");
        builder.HasIndex(x => x.LastMessageAt).HasDatabaseName("ix_conversations_last_message_at");
    }
}
