using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Persistence.Configurations;

public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("chat_messages");
        builder.HasKey(x => x.MessageId);
        builder.Property(x => x.MessageId).HasColumnName("message_id");
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id").IsRequired();
        builder.Property(x => x.SenderId).HasColumnName("sender_id").IsRequired();
        builder.Property(x => x.Body).HasColumnName("body").HasColumnType("text").HasMaxLength(4000).IsRequired();
        builder.Property(x => x.ClientMessageId).HasColumnName("client_message_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.EditedAt).HasColumnName("edited_at");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.HasOne(x => x.Conversation).WithMany(x => x.Messages).HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Sender).WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ConversationId, x.CreatedAt }).HasDatabaseName("ix_chat_messages_conversation_created_at");
        builder.HasIndex(x => new { x.SenderId, x.ClientMessageId }).IsUnique().HasDatabaseName("ux_chat_messages_sender_client_message_id");
    }
}
