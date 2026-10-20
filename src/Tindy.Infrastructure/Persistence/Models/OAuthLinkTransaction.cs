namespace Tindy.Infrastructure.Persistence.Models;

/// <summary>Two-step, single-use OAuth account linking transaction.</summary>
public sealed class OAuthLinkTransaction
{
    public Guid TransactionId { get; set; }
    public string StartTicketHash { get; set; } = null!;
    public string? CompletionTicketHash { get; set; }
    public Guid UserId { get; set; }
    public string Provider { get; set; } = null!;
    public string Status { get; set; } = "pending";
    public string? ProviderUserId { get; set; }
    public string? ProviderUsername { get; set; }
    public string? DisplayName { get; set; }
    public string? ProviderEmail { get; set; }
    public string? AvatarUrl { get; set; }
    public bool EmailVerified { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
