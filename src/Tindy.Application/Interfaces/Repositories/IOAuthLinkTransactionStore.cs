using Tindy.Application.Models.Authentication;

namespace Tindy.Application.Interfaces.Repositories;

public interface IOAuthLinkTransactionStore
{
    Task<string> IssueStartAsync(Guid userId, string provider, CancellationToken cancellationToken = default);
    Task<Guid?> ConsumeStartAsync(string startToken, string provider, CancellationToken cancellationToken = default);
    Task<string?> CompleteProviderAsync(Guid transactionId, AuthenticatedExternalIdentity identity,
        CancellationToken cancellationToken = default);
    Task<AuthenticatedExternalIdentity?> ConsumeCompletionAsync(string linkTicket, Guid userId,
        CancellationToken cancellationToken = default);
}
