using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Models.Authentication;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

public sealed class AccountConnectionsRepository : IAccountConnectionsRepository
{
    private readonly TindyDbContext _db;
    public AccountConnectionsRepository(TindyDbContext db) => _db = db;

    public async Task<AccountConnectionsResult?> GetForActiveUserAsync(Guid userId,
        CancellationToken cancellationToken = default)
    {
        var active = await _db.Users.AsNoTracking().AnyAsync(x => x.UserId == userId &&
            x.DeletedAt == null && x.Status == "active", cancellationToken);
        if (!active) return null;
        var hasLocal = await _db.UserCredentials.AsNoTracking()
            .AnyAsync(x => x.UserId == userId, cancellationToken);
        var providers = await _db.ExternalAuthAccounts.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.Provider)
            .Select(x => new ConnectedProvider(x.Provider, x.ProviderUsername, x.CreatedAt))
            .ToListAsync(cancellationToken);
        return new AccountConnectionsResult(hasLocal, providers);
    }

    public async Task UnlinkKeepingLastMethodAsync(Guid userId, string provider,
        CancellationToken cancellationToken = default)
    {
        // SERIALIZABLE transactions prevent two concurrent requests from each
        // seeing two linked providers and deleting both (write-skew anomaly).
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var active = await _db.Users.AsNoTracking().AnyAsync(x => x.UserId == userId &&
                x.DeletedAt == null && x.Status == "active", cancellationToken);
            if (!active) throw new InvalidCredentialsException();
            var linked = await _db.ExternalAuthAccounts.AsNoTracking()
                .AnyAsync(x => x.UserId == userId && x.Provider == provider, cancellationToken);
            if (!linked) throw new NotFoundException("This provider is not linked to your account.");

            var hasLocal = await _db.UserCredentials.AsNoTracking()
                .AnyAsync(x => x.UserId == userId, cancellationToken);
            var externalCount = await _db.ExternalAuthAccounts.AsNoTracking()
                .CountAsync(x => x.UserId == userId, cancellationToken);
            if (!hasLocal && externalCount <= 1)
                throw new ConflictException("Cannot unlink the final sign-in method.");

            var deleted = await _db.ExternalAuthAccounts.Where(x => x.UserId == userId &&
                x.Provider == provider).ExecuteDeleteAsync(cancellationToken);
            if (deleted != 1)
                throw new ConflictException("Provider connection changed. Please retry.");
            await tx.CommitAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            throw new ConflictException("Concurrent account changes detected. Please retry.", ex);
        }
    }
}
