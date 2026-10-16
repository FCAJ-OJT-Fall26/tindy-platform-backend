namespace Tindy.Application.Models.Authentication;

/// <summary>
/// Identity resolution result, NOT a JWT or a browser session.
/// The STEP 04 ticket exchange will issue a Tindy JWT after consuming the ticket.
/// </summary>
public sealed record ExternalSignInResult(
    Guid UserId,
    bool IsNewUser,
    string DisplayName,
    string? PrimaryEmail,
    IReadOnlyCollection<string> Roles,
    bool IsOnboardingCompleted);
