namespace Tindy.Application.Entities;

public class ProfileExperience
{
    public Guid ExperienceId { get; set; }
    public Guid UserId { get; set; }
    public string ProjectName { get; set; } = null!;
    public string RoleTitle { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string? RepoUrl { get; set; }
    // Month-only API dates are stored as the first day of their month.
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsOngoing { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public ICollection<ProfileExperienceSkill> ExperienceSkills { get; set; } = new List<ProfileExperienceSkill>();
}
