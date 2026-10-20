using Tindy.Application.DTOs.Profile;

namespace Tindy.Application.Interfaces.Services;

public interface IProfileService
{
    Task<MyProfileResponse> GetMyProfileAsync(Guid userId, CancellationToken cancellationToken = default);
}
