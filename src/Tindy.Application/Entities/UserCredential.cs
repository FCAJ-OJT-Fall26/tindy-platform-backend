namespace Tindy.Application.Entities;

public class UserCredential
{
    public Guid UserCredentialId { get; set; }

    public Guid UserId { get; set; }

    public string Email { get; set; } = null!;

    public string EmailNormalized { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public DateTime? EmailVerifiedAt { get; set; }

    public DateTime? PasswordChangedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public User User { get; set; } = null!;
}