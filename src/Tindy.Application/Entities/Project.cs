namespace Tindy.Application.Entities;

public class Project
{
    public Guid ProjectId { get; set; }
    public Guid LeaderId { get; set; }
    public string Name { get; set; } = null!;
    public string? Tagline { get; set; }
    public string Description { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string DifficultyLevel { get; set; } = "intermediate"; // beginner_friendly, intermediate, advanced
    public int ExpectedCommitmentHours { get; set; } = 10;
    public string Duration { get; set; } = "3 months";
    public int InitialTeamSize { get; set; } = 1;
    public int MaxTeamCapacity { get; set; } = 5;
    public string? ColorTheme { get; set; } = "indigo";
    public string Status { get; set; } = "recruiting"; // draft, recruiting, in_progress, completed, archived
    public DateTime RecruitmentClosesAt { get; set; }
    public List<string> Goals { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }

    public User Leader { get; set; } = null!;
    public ICollection<ProjectMember> Members { get; set; } = new List<ProjectMember>();
    public ICollection<ProjectPosition> Positions { get; set; } = new List<ProjectPosition>();
    public ICollection<DiscoveryProjectInteraction> DiscoveryInteractions { get; set; } = new List<DiscoveryProjectInteraction>();
    public ICollection<ProjectApplication> Applications { get; set; } = new List<ProjectApplication>();
}
