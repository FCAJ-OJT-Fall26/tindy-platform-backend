using Tindy.Application.Entities;

namespace Tindy.Application.Interfaces.Repositories;

public interface IExternalAuthRepository
{
    Task<ExternalAuthAccount?> GetByProviderIdentityAsync(
        string provider, string providerUserId, CancellationToken cancellationToken = default);

    Task<ExternalAuthAccount?> GetByUserAndProviderAsync(
        Guid userId, string provider, CancellationToken cancellationToken = default);

    Task<User?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> EmailReservedAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    Task<Role?> GetRoleByNameAsync(string roleName, CancellationToken cancellationToken = default);

    Task AddUserAsync(User user, CancellationToken cancellationToken = default);

    Task AddExternalAccountAsync(ExternalAuthAccount account, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the complete new-user aggregate atomically in one EF Core SaveChanges call.
    /// Known unique constraint violations translate to ConflictException.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
