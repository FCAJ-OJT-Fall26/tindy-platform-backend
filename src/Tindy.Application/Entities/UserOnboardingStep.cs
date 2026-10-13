
namespace Tindy.Application.Entities;

public class UserOnboardingStep
{
    public Guid UserId { get; set; }

    public int StepNumber { get; set; }

    public DateTime CompletedAt { get; set; }

    public User User { get; set; } = null!;
}
