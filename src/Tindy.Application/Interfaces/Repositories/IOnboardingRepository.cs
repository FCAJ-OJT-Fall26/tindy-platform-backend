using Tindy.Application.Entities;

namespace Tindy.Application.Interfaces.Repositories;

public interface IOnboardingRepository
{
    // Includes Profile, Skill links, Steps, Credential and Roles.
    // Entities are tracked so Application can update the aggregate.
    Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> UsernameExistsAsync(Guid exceptUserId, string usernameNormalized, CancellationToken cancellationToken = default);

    // Must run inside ExecuteInTransactionAsync. Concurrent catalog creation
    // is safe because PostgreSQL uses ON CONFLICT DO NOTHING.
    Task<IReadOnlyList<Skill>> GetOrCreateSkillsAsync(IReadOnlyCollection<string> skillNames, CancellationToken cancellationToken = default);

    void SyncUserSkills(UserProfile profile, IReadOnlyCollection<Skill> skills);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    // Serializes concurrent writes for the same user and rolls back on failure.
    Task<T> ExecuteInTransactionAsync<T>(Guid userId, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
}
