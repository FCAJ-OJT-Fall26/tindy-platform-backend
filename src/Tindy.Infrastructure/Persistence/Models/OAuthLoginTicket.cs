namespace Tindy.Infrastructure.Persistence.Models;

/// <summary>
/// Only a SHA-256 hash is persisted. Raw ticket never leaves the browser and exchange response.
/// </summary>
public sealed class OAuthLoginTicket
{
    public string TicketHash { get; set; } = null!;
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
