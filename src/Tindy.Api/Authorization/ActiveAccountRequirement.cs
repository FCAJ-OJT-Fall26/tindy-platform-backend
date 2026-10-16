using Microsoft.AspNetCore.Authorization;

namespace Tindy.Api.Authorization;

/// <summary>
/// Allows valid Tindy JWTs to use setup APIs only while the account exists
/// and remains active (onboarding completion is not required).
/// </summary>
public sealed class ActiveAccountRequirement : IAuthorizationRequirement
{
}
