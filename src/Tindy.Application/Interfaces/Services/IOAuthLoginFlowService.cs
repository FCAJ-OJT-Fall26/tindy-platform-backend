using Tindy.Application.Models.Authentication;

namespace Tindy.Application.Interfaces.Services;

public interface IOAuthLoginFlowService
{
    Task<string> BeginTicketAsync(AuthenticatedExternalIdentity identity,
        CancellationToken cancellationToken = default);
    Task<OAuthExchangeResponse> ExchangeAsync(string ticket,
        CancellationToken cancellationToken = default);
}
