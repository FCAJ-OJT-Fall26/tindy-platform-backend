
namespace Tindy.Application.DTOs.Authentication;

public sealed record LoginResponse(string AccessToken, string TokenType, int ExpiresIn, LoginUserResponse User, string RedirectTo);

public sealed record LoginUserResponse(Guid UserId, string DisplayName, string University, string Email, string? Username, string Status, IReadOnlyCollection<string> Roles, bool IsOnboardingCompleted);
