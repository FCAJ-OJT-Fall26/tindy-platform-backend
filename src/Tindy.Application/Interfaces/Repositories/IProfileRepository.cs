using Tindy.Application.DTOs.Profile;

namespace Tindy.Application.Interfaces.Repositories;

// Read projection for the current account; no tracked User/credential/OAuth graph.
public interface IProfileRepository
{
    Task<MyProfileResponse?> GetMyProfileAsync(Guid userId, CancellationToken cancellationToken = default);
}
