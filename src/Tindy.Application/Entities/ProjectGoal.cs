namespace Tindy.Application.Entities;

public class ProjectGoal
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = null!;
    public string Tag { get; set; } = "Milestone";
    public string DueDate { get; set; } = "TBD";
    public bool IsCompleted { get; set; } = false;
    public List<Guid> AssigneeIds { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public Project Project { get; set; } = null!;
}
