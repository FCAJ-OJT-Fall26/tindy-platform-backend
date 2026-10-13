namespace Tindy.Application.DTOs.Onboarding;

public sealed record CurrentUserResponse(
    Guid UserId,
    string? Username,
    string DisplayName,
    string? University,
    string Email,
    string Status,
    IReadOnlyCollection<string> Roles,
    bool IsOnboardingCompleted);

public sealed record OnboardingDraftResponse(
    int TotalSteps,
    IReadOnlyList<int> CompletedSteps,
    int CurrentStep,
    bool IsOnboardingCompleted,
    IReadOnlyDictionary<string, object> StepData);

public sealed record OnboardingSaveResponse(
    int SavedStep,
    IReadOnlyList<int> CompletedSteps,
    int NextStep,
    bool IsOnboardingCompleted);

public sealed record OnboardingCompleteResponse(
    bool IsOnboardingCompleted,
    IReadOnlyList<int> CompletedSteps,
    string RedirectTo);
