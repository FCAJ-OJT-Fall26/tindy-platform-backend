
namespace Tindy.Application.DTOs.Authentication;

public sealed record RegisterResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    RegisterUserResponse User,
    string RedirectTo);

public sealed record RegisterUserResponse(
    Guid UserId,
    string DisplayName,
    string University,
    string Email,
    string? Username,
    string Status,
    IReadOnlyCollection<string> Roles,
    bool IsOnboardingCompleted);
