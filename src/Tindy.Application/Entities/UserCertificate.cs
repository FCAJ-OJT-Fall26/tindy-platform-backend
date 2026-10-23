namespace Tindy.Application.Entities;

public class UserCertificate
{
    public Guid CertificateId { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = null!;
    public string Issuer { get; set; } = null!;
    public string? Description { get; set; }
    public string? CredentialUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public ICollection<UserCertificateAttachment> Attachments { get; set; } = new List<UserCertificateAttachment>();
}
