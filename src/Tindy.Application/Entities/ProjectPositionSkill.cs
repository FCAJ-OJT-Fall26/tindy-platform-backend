namespace Tindy.Application.Entities;

public class ProjectPositionSkill
{
    public Guid PositionId { get; set; }
    public Guid SkillId { get; set; }
    public bool IsRequired { get; set; } = true;
    public string? MinimumExperienceLevel { get; set; }

    public ProjectPosition Position { get; set; } = null!;
    public Skill Skill { get; set; } = null!;
}
