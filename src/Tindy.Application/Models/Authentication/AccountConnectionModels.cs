namespace Tindy.Application.Models.Authentication;

public sealed record ConnectedProvider(string Provider, string? ProviderUsername, DateTime LinkedAt);
public sealed record AccountConnectionsResult(bool HasLocalPassword, IReadOnlyList<ConnectedProvider> Providers);
