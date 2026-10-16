namespace Tindy.Api.Authorization;

public static class OnboardingPolicies
{
    // JWT + active account, without requiring completed onboarding.
    // Used by current-user/onboarding, logout, and OAuth account connections.
    public const string AuthenticatedOnly = "AuthenticatedOnly";

    // Error code returned when valid JWT is used before completing onboarding.
    public const string OnboardingRequired = "ONBOARDING_REQUIRED";
}
