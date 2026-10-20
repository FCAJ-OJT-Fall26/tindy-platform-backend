using Tindy.Application.DTOs.Profile;
using Tindy.Application.Entities;

namespace Tindy.Application.Interfaces.Repositories;

public interface IProfileRepository
{
    Task<MyProfileResponse?> GetMyProfileAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<User?> GetUserForUpdateAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> IsActiveProfessionalRoleAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Guid>> GetActiveSkillIdsAsync(IReadOnlyCollection<Guid> skillIds, CancellationToken cancellationToken = default);

    void SyncUserSkills(UserProfile profile, IReadOnlyCollection<Guid> skillIds);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<T> ExecuteInTransactionAsync<T>(Guid userId, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
}
