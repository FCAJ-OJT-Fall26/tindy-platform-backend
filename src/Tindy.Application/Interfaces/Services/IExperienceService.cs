using Tindy.Application.DTOs.Profile;

namespace Tindy.Application.Interfaces.Services;

// Caller userId must come from an authenticated JWT, never a client-supplied request body.
public interface IExperienceService
{
    Task<IReadOnlyList<ExperienceResponse>> ListMyExperiencesAsync(Guid callerUserId, CancellationToken cancellationToken = default);
    Task<ExperienceResponse> GetMyExperienceAsync(Guid callerUserId, Guid experienceId, CancellationToken cancellationToken = default);
    Task<ExperienceResponse> CreateAsync(Guid callerUserId, ExperienceUpsertRequest request, CancellationToken cancellationToken = default);
    Task<ExperienceResponse> UpdateAsync(Guid callerUserId, Guid experienceId, ExperienceUpsertRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid callerUserId, Guid experienceId, CancellationToken cancellationToken = default);
}
