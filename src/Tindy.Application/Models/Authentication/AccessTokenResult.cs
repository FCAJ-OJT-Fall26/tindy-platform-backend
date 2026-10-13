
namespace Tindy.Application.Models.Authentication;

public sealed record AccessTokenResult(string AccessToken, string TokenType, int ExpiresIn);
