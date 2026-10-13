
namespace Tindy.Application.Entities;

public class UserSkill
{
    public Guid UserId { get; set; }

    public Guid SkillId { get; set; }

    public UserProfile UserProfile { get; set; } = null!;

    public Skill Skill { get; set; } = null!;
}
