namespace Tindy.Application.Entities;

public class ProjectMember
{
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public Guid? PositionId { get; set; }
    public string RoleTitle { get; set; } = null!;
    public bool IsLeader { get; set; } = false;
    public DateTime JoinedAt { get; set; }
    public string Status { get; set; } = "active"; // active, left, removed

    public Project Project { get; set; } = null!;
    public User User { get; set; } = null!;
    public ProjectPosition? Position { get; set; }
}
