namespace Tindy.Application.Entities;

public class SkillCategory
{
    public Guid CategoryId { get; set; }

    public string Name { get; set; } = null!;

    public string NameNormalized { get; set; } = null!;

    public string? Description { get; set; }

    public int DisplayOrder { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public ICollection<SkillCategorySkill> SkillCategorySkills { get; set; }
        = new List<SkillCategorySkill>();
}
