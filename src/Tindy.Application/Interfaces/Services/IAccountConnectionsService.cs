using Tindy.Application.Models.Authentication;

namespace Tindy.Application.Interfaces.Services;

public interface IAccountConnectionsService
{
    Task<AccountConnectionsResult> GetAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<string> BeginAsync(Guid userId, string provider, CancellationToken cancellationToken = default);
    Task<Guid?> BeginChallengeAsync(string startToken, string provider, CancellationToken cancellationToken = default);
    Task<string?> FinishProviderAsync(Guid transactionId, AuthenticatedExternalIdentity identity,
        CancellationToken cancellationToken = default);
    Task<bool> ConfirmAsync(Guid userId, string linkTicket, CancellationToken cancellationToken = default);
    Task UnlinkAsync(Guid userId, string provider, CancellationToken cancellationToken = default);
}
