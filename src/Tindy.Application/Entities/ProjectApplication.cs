namespace Tindy.Application.Entities;

public sealed class ProjectApplication
{
    public Guid ApplicationId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid PositionId { get; set; }
    public Guid ApplicantId { get; set; }
    public string Status { get; set; } = "pending";
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public Project Project { get; set; } = null!;
    public ProjectPosition Position { get; set; } = null!;
    public User Applicant { get; set; } = null!;
    public User? Reviewer { get; set; }
}
