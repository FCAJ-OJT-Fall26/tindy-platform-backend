using Tindy.Application.DTOs.Profile;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Application.Services;

public sealed class ProfileService : IProfileService
{
    private readonly IProfileRepository _profileRepository;

    public ProfileService(IProfileRepository profileRepository) => _profileRepository = profileRepository;

    public async Task<MyProfileResponse> GetMyProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            throw new InvalidCredentialsException();

        // The DB read also filters suspended/deleted/incomplete accounts and missing profiles.
        return await _profileRepository.GetMyProfileAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Profile was not found.");
    }
}
