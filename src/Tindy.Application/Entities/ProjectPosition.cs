namespace Tindy.Application.Entities;

public class ProjectPosition
{
    public Guid PositionId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid RoleId { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public int RequiredCount { get; set; } = 1;
    public int FilledCount { get; set; } = 0;
    public string ExperienceLevel { get; set; } = "intermediate"; // beginner_friendly, intermediate, advanced
    public string Status { get; set; } = "open"; // open, filled, closed
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public Project Project { get; set; } = null!;
    public ProfessionalRole Role { get; set; } = null!;
    public ICollection<ProjectPositionSkill> PositionSkills { get; set; } = new List<ProjectPositionSkill>();
}
