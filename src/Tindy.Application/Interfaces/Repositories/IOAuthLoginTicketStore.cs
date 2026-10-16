namespace Tindy.Application.Interfaces.Repositories;

/// <summary>
/// Distributed, short-lived one-time ticket store. Never persists raw bearer tickets.
/// The ticket is not a JWT or a Google/GitHub access token.
/// </summary>
public interface IOAuthLoginTicketStore
{
    Task<string> IssueAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Guid?> ConsumeAsync(string ticket, CancellationToken cancellationToken = default);
}
