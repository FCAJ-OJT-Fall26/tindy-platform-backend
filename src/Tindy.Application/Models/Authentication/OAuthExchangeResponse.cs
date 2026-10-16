namespace Tindy.Application.Models.Authentication;

public sealed record OAuthExchangeResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    OAuthExchangeUser User,
    string RedirectTo);

public sealed record OAuthExchangeUser(
    Guid UserId,
    string DisplayName,
    string? Email,
    string? Username,
    string Status,
    IReadOnlyCollection<string> Roles,
    bool IsOnboardingCompleted);
