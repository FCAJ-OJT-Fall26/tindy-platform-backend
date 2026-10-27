namespace Tindy.Application.Entities;

public class ProfessionalRole
{
    public Guid ProfessionalRoleId { get; set; }

    public string Name { get; set; } = null!;

    public string NameNormalized { get; set; } = null!;

    public string? Description { get; set; }

    public int DisplayOrder { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public ICollection<UserProfile> UserProfiles { get; set; }
        = new List<UserProfile>();

    public ICollection<ProjectPosition> ProjectPositions { get; set; }
        = new List<ProjectPosition>();
}
