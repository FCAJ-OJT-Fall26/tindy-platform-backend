using Tindy.Application.DTOs.Profile;

namespace Tindy.Application.Interfaces.Services;

public interface IProfileService
{
    Task<MemberSearchResponse> SearchMembersAsync(string? query, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default);
    Task<MyProfileResponse> GetMyProfileAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<MemberProfileResponse> GetMemberProfileAsync(Guid targetUserId, CancellationToken cancellationToken = default);

    Task<MyProfileResponse> UpdateMyProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);
}
