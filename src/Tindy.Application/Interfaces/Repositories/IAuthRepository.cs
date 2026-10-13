
using Tindy.Application.Entities;

namespace Tindy.Application.Interfaces.Repositories;

public interface IAuthRepository
{
    // ===================================
    // 1. CHECK EXISTENCE
    // ===================================

    Task<bool> EmailExistsAsync(string emailNormalized, CancellationToken cancellationToken = default);

    Task<bool> UsernameExistsAsync(string usernameNormalized, CancellationToken cancellationToken = default);

    // ===================================
    // 2. FIND USERS
    // ===================================

    Task<User?> GetByEmailAsync(string emailNormalized, CancellationToken cancellationToken = default);

    Task<User?> GetByUsernameAsync(string usernameNormalized, CancellationToken cancellationToken = default);

    Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    // ===================================
    // 3. FIND DEFAULT ROLE
    // ===================================

    Task<Role?> GetRoleByNameAsync(string roleName, CancellationToken cancellationToken = default);

    // ===================================
    // 4. WRITE OPERATIONS
    // ===================================

    Task AddUserAsync(User user, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
