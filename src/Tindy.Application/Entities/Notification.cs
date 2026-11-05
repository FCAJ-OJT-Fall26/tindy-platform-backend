using System.Text.Json;

namespace Tindy.Application.Entities;

public sealed class Notification
{
    public Guid NotificationId { get; set; }
    public Guid RecipientUserId { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid InvitationId { get; set; }
    public string Type { get; set; } = null!;
    public JsonElement Payload { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public User Recipient { get; set; } = null!;
    public User? Actor { get; set; }
    public ProjectInvitation Invitation { get; set; } = null!;
}
