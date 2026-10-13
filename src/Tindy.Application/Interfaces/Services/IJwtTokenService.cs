
using Tindy.Application.Models.Authentication;

namespace Tindy.Application.Interfaces.Services;

public interface IJwtTokenService
{
    AccessTokenResult GenerateAccessToken(Guid userId, IReadOnlyCollection<string> roles);
}
