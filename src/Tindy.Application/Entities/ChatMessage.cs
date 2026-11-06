namespace Tindy.Application.Entities;

public sealed class ChatMessage
{
    public Guid MessageId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid SenderId { get; set; }
    public string? ContextInputKey { get; set; }
    public Guid? ContextProjectId { get; set; }
    public string? ContextProjectName { get; set; }
    public string? ContextPositionTitle { get; set; }
    public string MessageType { get; set; } = "text";
    public string Body { get; set; } = null!;
    public Guid? ClientMessageId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public User Sender { get; set; } = null!;
}
