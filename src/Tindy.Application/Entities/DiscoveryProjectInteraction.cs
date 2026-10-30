namespace Tindy.Application.Entities;

public sealed class DiscoveryProjectInteraction
{
    public Guid UserId { get; set; }
    public Guid ProjectId { get; set; }
    public bool IsSaved { get; set; }
    public bool IsSkipped { get; set; }
    public DateTime UpdatedAt { get; set; }
    public User User { get; set; } = null!;
    public Project Project { get; set; } = null!;
}
