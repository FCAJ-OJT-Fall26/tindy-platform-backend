using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

public sealed class ExternalAuthRepository : IExternalAuthRepository
{
    private readonly TindyDbContext _context;

    public ExternalAuthRepository(TindyDbContext context)
    {
        _context = context;
    }

    public Task<ExternalAuthAccount?> GetByProviderIdentityAsync(
        string provider, string providerUserId, CancellationToken cancellationToken = default) =>
        _context.ExternalAuthAccounts.AsNoTracking()
            .Include(x => x.User).ThenInclude(x => x.Profile)
            .Include(x => x.User).ThenInclude(x => x.UserRoles).ThenInclude(x => x.Role)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Provider == provider && x.ProviderUserId == providerUserId,
                cancellationToken);

    public Task<ExternalAuthAccount?> GetByUserAndProviderAsync(
        Guid userId, string provider, CancellationToken cancellationToken = default) =>
        _context.ExternalAuthAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Provider == provider, cancellationToken);

    public Task<User?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _context.Users.AsNoTracking()
            .Include(x => x.Profile)
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

    public async Task<bool> EmailReservedAsync(
        string normalizedEmail, CancellationToken cancellationToken = default) =>
        await _context.Users.AsNoTracking()
            .AnyAsync(x => x.PrimaryEmailNormalized == normalizedEmail, cancellationToken)
        || await _context.UserCredentials.AsNoTracking()
            .AnyAsync(x => x.EmailNormalized == normalizedEmail, cancellationToken);

    public Task<Role?> GetRoleByNameAsync(string roleName, CancellationToken cancellationToken = default) =>
        _context.Roles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RoleName == roleName, cancellationToken);

    public async Task AddUserAsync(User user, CancellationToken cancellationToken = default)
    {
        await _context.Users.AddAsync(user, cancellationToken);
    }

    public async Task AddExternalAccountAsync(ExternalAuthAccount account,
        CancellationToken cancellationToken = default)
    {
        await _context.ExternalAuthAccounts.AddAsync(account, cancellationToken);
    }

    public async Task AddExternalAccountAndFillAvatarAsync(ExternalAuthAccount account, string? avatarUrl,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await AddExternalAccountAsync(account, cancellationToken);
        await SaveChangesAsync(cancellationToken);

        if (avatarUrl is not null)
        {
            DateTime? now = DateTime.UtcNow;
            await _context.UserProfiles
                .Where(x => x.UserId == account.UserId && x.AvatarUrl == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.AvatarUrl, avatarUrl)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ux_external_auth_accounts_provider_user"
                    or "ux_external_auth_accounts_user_provider"
                    or "ux_users_primary_email_normalized"
                    or "ux_user_credentials_email_normalized"
            })
        {
            // Concurrent OAuth registrations/linking must never silently merge accounts.
            // Never disclose the colliding provider identity or email in the response.
            throw new ConflictException("External identity or account email conflict. Sign in and link explicitly.", ex);
        }
    }
}
