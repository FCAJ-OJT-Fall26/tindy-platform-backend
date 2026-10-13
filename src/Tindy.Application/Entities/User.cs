namespace Tindy.Application.Entities;

public class User
{
    public Guid UserId {get; set;}
    public string? Username {get; set;}
    public string? UsernameNormalized {get; set;}
    public string Status {get; set;} = null!;
    public bool IsOnboardingCompleted { get; set; } = false;
    public DateTime CreatedAt {get; set;} 
    public DateTime? UpdatedAt {get;set;}
    public DateTime? DeletedAt {get;set;}
    public UserProfile? Profile { get; set; }
    public UserCredential? Credential { get; set; }
    public ICollection<UserRole> UserRoles { get; set; }
    = new List<UserRole>();
    public ICollection<ExternalAuthAccount> ExternalAuthAccounts { get; set; }
    = new List<ExternalAuthAccount>();   
    public ICollection<UserOnboardingStep> OnboardingSteps
    { get; set; } = new List<UserOnboardingStep>();
}