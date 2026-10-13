namespace Tindy.Application.Exceptions;

public sealed class OnboardingIncompleteException : BusinessRuleException
{
    public IReadOnlyList<int> MissingSteps { get; }

    public OnboardingIncompleteException(IReadOnlyList<int> missingSteps)
        : base("Required onboarding steps are incomplete or contain invalid data.")
    {
        MissingSteps = missingSteps;
    }
}
