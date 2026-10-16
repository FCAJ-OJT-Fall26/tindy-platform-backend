using Tindy.Application.Models.Authentication;

namespace Tindy.Application.Interfaces.Repositories;

public interface IAccountConnectionsRepository
{
    /// <summary>Returns null if User is absent, inactive or soft deleted.</summary>
    Task<AccountConnectionsResult?> GetForActiveUserAsync(Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically enforces that a User keeps one valid login method.</summary>
    Task UnlinkKeepingLastMethodAsync(Guid userId, string provider,
        CancellationToken cancellationToken = default);
}
