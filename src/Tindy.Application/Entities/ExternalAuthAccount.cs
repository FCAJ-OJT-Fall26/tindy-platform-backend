namespace Tindy.Application.Entities;

public class ExternalAuthAccount
{
    public Guid ExternalAuthAccountId { get; set; }

    public Guid UserId { get; set; }

    public string Provider { get; set; } = null!;

    public string ProviderUserId { get; set; } = null!;

    public string? ProviderUsername { get; set; }

    public string? ProviderEmail { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public User User { get; set; } = null!;
}