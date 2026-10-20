using Tindy.Application.DTOs.Profile;
using Tindy.Application.Entities;

namespace Tindy.Application.Interfaces.Repositories;

public interface IExperienceRepository
{
    Task<bool> IsEligibleOwnerAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExperienceResponse>> ListOwnedAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ExperienceResponse?> GetOwnedAsync(Guid userId, Guid experienceId, CancellationToken cancellationToken = default);
    Task<ProfileExperience?> FindOwnedForUpdateAsync(Guid userId, Guid experienceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Guid>> GetActiveSkillIdsAsync(IReadOnlyCollection<Guid> skillIds, CancellationToken cancellationToken = default);
    void Add(ProfileExperience experience);
    void SyncSkills(ProfileExperience experience, IReadOnlyCollection<Guid> skillIds);
    void Remove(ProfileExperience experience);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<T> ExecuteInTransactionAsync<T>(Guid ownerUserId, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
}
