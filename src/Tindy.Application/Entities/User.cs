namespace Tindy.Application.Entities;

public class User
{
    public Guid UserId {get; set;}
    public string? Username {get; set;}
    public string? UsernameNormalized {get; set;}

    // Private contact email; independent from Local password credentials.
    // May be null when an OAuth provider returns no verified email.
    public string? PrimaryEmail { get; set; }
    public string? PrimaryEmailNormalized { get; set; }
    public DateTime? PrimaryEmailVerifiedAt { get; set; }
    public string Status {get; set;} = null!;
    public bool IsOnboardingCompleted { get; set; } = false;
    public DateTime CreatedAt {get; set;} 
    public DateTime? UpdatedAt {get;set;}
    public DateTime? DeletedAt {get;set;}
    public ICollection<MediaAsset> MediaAssets { get; set; } = new List<MediaAsset>();
    public UserProfile? Profile { get; set; }
    public ICollection<ProfileExperience> ProfileExperiences { get; set; } = new List<ProfileExperience>();
    public ICollection<UserCertificate> Certificates { get; set; } = new List<UserCertificate>();
    public UserCredential? Credential { get; set; }
    public ICollection<UserRole> UserRoles { get; set; }
    = new List<UserRole>();
    public ICollection<ExternalAuthAccount> ExternalAuthAccounts { get; set; }
    = new List<ExternalAuthAccount>();   
    public ICollection<UserOnboardingStep> OnboardingSteps
    { get; set; } = new List<UserOnboardingStep>();
}