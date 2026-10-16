using Tindy.Application.Constants;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Authentication;

namespace Tindy.Application.Services;

/// <summary>
/// OAuth callback never links an account by itself. A one-time completion ticket and
/// the SAME authenticated Tindy UserId are both required to confirm linking.
/// </summary>
public sealed class AccountConnectionsService : IAccountConnectionsService
{
    private readonly IAccountConnectionsRepository _connections;
    private readonly IOAuthLinkTransactionStore _linkStore;
    private readonly IExternalAuthService _externalAuth;

    public AccountConnectionsService(IAccountConnectionsRepository connections,
        IOAuthLinkTransactionStore linkStore, IExternalAuthService externalAuth)
    {
        _connections = connections;
        _linkStore = linkStore;
        _externalAuth = externalAuth;
    }

    public async Task<AccountConnectionsResult> GetAsync(Guid userId,
        CancellationToken cancellationToken = default) =>
        await _connections.GetForActiveUserAsync(userId, cancellationToken)
        ?? throw new InvalidCredentialsException();

    public async Task<string> BeginAsync(Guid userId, string provider,
        CancellationToken cancellationToken = default)
    {
        provider = ExternalAuthProviders.Normalize(provider);
        _ = await GetAsync(userId, cancellationToken);
        return await _linkStore.IssueStartAsync(userId, provider, cancellationToken);
    }

    public Task<Guid?> BeginChallengeAsync(string startToken, string provider,
        CancellationToken cancellationToken = default) =>
        _linkStore.ConsumeStartAsync(startToken, ExternalAuthProviders.Normalize(provider), cancellationToken);

    public Task<string?> FinishProviderAsync(Guid transactionId, AuthenticatedExternalIdentity identity,
        CancellationToken cancellationToken = default) =>
        _linkStore.CompleteProviderAsync(transactionId, identity, cancellationToken);

    public async Task<bool> ConfirmAsync(Guid userId, string linkTicket,
        CancellationToken cancellationToken = default)
    {
        _ = await GetAsync(userId, cancellationToken);
        var identity = await _linkStore.ConsumeCompletionAsync(linkTicket, userId, cancellationToken)
            ?? throw new InvalidCredentialsException();
        // Existing STEP 03 LinkAsync already rejects cross-account identity takeover
        // and is idempotent for the very same provider identity.
        return await _externalAuth.LinkAsync(userId, identity, cancellationToken);
    }

    public Task UnlinkAsync(Guid userId, string provider, CancellationToken cancellationToken = default) =>
        _connections.UnlinkKeepingLastMethodAsync(userId, ExternalAuthProviders.Normalize(provider), cancellationToken);
}
