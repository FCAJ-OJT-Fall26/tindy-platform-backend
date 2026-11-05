namespace Tindy.Application.Entities;

public sealed class TeammateDiscoveryInteraction
{
    public Guid ProjectId { get; set; }
    public Guid PositionId { get; set; }
    public Guid CandidateUserId { get; set; }
    public string Action { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Project Project { get; set; } = null!;
    public ProjectPosition Position { get; set; } = null!;
    public User Candidate { get; set; } = null!;
}
