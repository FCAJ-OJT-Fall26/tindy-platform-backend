namespace Tindy.Application.Entities;

public class ProfileExperienceSkill
{
    public Guid ExperienceId { get; set; }
    public Guid SkillId { get; set; }

    public ProfileExperience Experience { get; set; } = null!;
    public Skill Skill { get; set; } = null!;
}
