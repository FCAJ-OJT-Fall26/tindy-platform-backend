namespace Tindy.Application.Entities;

public sealed class ProjectInvitation
{
    public Guid InvitationId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid PositionId { get; set; }
    public Guid InvitedUserId { get; set; }
    public Guid InvitingLeaderId { get; set; }
    public string? Message { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public Project Project { get; set; } = null!;
    public ProjectPosition Position { get; set; } = null!;
    public User InvitedUser { get; set; } = null!;
    public User InvitingLeader { get; set; } = null!;
}
