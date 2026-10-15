namespace Tindy.Application.Entities;

public class SkillCategorySkill
{
    public Guid CategoryId { get; set; }

    public Guid SkillId { get; set; }

    public SkillCategory SkillCategory { get; set; } = null!;

    public Skill Skill { get; set; } = null!;
}
