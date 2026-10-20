namespace Tindy.Application.Exceptions;

public sealed class OnboardingAlreadyCompletedException : ConflictException
{
    public OnboardingAlreadyCompletedException()
        : base("Onboarding has already been completed. Update your information through the profile API.")
    {
    }
}
