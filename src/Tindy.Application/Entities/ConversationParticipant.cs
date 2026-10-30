namespace Tindy.Application.Entities;

public sealed class ConversationParticipant
{
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? LastReadAt { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public User User { get; set; } = null!;
}
