
using Microsoft.EntityFrameworkCore;
using Tindy.Application.Entities;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Persistence;
using Npgsql;
using Tindy.Application.Exceptions;

namespace Tindy.Infrastructure.Repositories;

public sealed class AuthRepository : IAuthRepository
{
    private readonly TindyDbContext _context;

    public AuthRepository(TindyDbContext context)
    {
        _context = context;
    }

    // ===================================
    // 1. EMAIL EXISTS
    // ===================================
    public async Task<bool> EmailExistsAsync(string emailNormalized, CancellationToken cancellationToken = default)
    {
        // Check both tables while supporting existing Local Credential records.
        // The new users index also protects against OAuth/Local email collisions.
        return await _context.Users
            .AsNoTracking()
            .AnyAsync(x => x.PrimaryEmailNormalized == emailNormalized, cancellationToken)
            || await _context.UserCredentials
                .AsNoTracking()
                .AnyAsync(x => x.EmailNormalized == emailNormalized, cancellationToken);
    }

    // ===================================
    // 2. USERNAME EXISTS
    // ===================================
    public async Task<bool> UsernameExistsAsync(string usernameNormalized, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AsNoTracking()
            .AnyAsync(x => x.UsernameNormalized == usernameNormalized, cancellationToken);
    }

    // ===================================
    // 3. GET USER BY EMAIL
    // ===================================

    public async Task<User?> GetByEmailAsync(string emailNormalized, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AsNoTracking()
            .Include(x => x.Profile)
            .Include(x => x.Credential)
            .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Credential != null && x.Credential.EmailNormalized == emailNormalized, cancellationToken);
    }


    // ===================================
    // 4. GET USER BY USERNAME
    // ===================================

    public async Task<User?> GetByUsernameAsync(string usernameNormalized, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AsNoTracking()
            .Include(x => x.Profile)
            .Include(x => x.Credential)
            .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.UsernameNormalized == usernameNormalized, cancellationToken);
    }


    // ===================================
    // 5. GET USER BY ID
    // ===================================
    public async Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AsNoTracking()
            .Include(x => x.Profile)
            .Include(x => x.Credential)
            .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
    }

    // ===================================
    // 6. GET ROLE BY NAME
    // ===================================
    public async Task<Role?> GetRoleByNameAsync(string roleName, CancellationToken cancellationToken = default)
    {
        return await _context.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RoleName == roleName, cancellationToken);
    }

    // ===================================
    // 7. ADD USER
    // ===================================
    public async Task AddUserAsync(User user, CancellationToken cancellationToken = default)
    {
        await _context.Users.AddAsync(user, cancellationToken);
    }

    // ===================================
    // 8. SAVE CHANGES
    // ===================================

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName:
                    "ux_user_credentials_email_normalized" or "ux_users_primary_email_normalized"
            })
        {
            throw new EmailAlreadyExistsException(ex);
        }
    }

}
