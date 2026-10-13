
namespace Tindy.Application.Entities;

public class Skill
{
    public Guid SkillId { get; set; }

    public string Name { get; set; } = null!;

    public string NameNormalized { get; set; } = null!;

    public ICollection<UserSkill> UserSkills { get; set; }
        = new List<UserSkill>();
}
