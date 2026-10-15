using System.ComponentModel.DataAnnotations;

namespace Tindy.Application.DTOs.Onboarding;

public sealed class OnboardingStep1Request
{
    [Required]
    [StringLength(50, MinimumLength = 3)]
    [RegularExpression(@"^[A-Za-z0-9_.]{3,50}$", ErrorMessage = "Username must contain only Latin letters, digits, underscores (_) or periods (.), and must be 3–50 characters long.")]
    public string? Username { get; set; }

    [Required]
    [StringLength(100)]
    public string? DisplayName { get; set; }

    [Required]
    [StringLength(255)]
    public string? University { get; set; }
}

public sealed class OnboardingStep2Request : IValidatableObject
{
    // IDs must come from GET /api/catalog/skills; users cannot create catalog skills.
    [Required]
    [MinLength(1)]
    [MaxLength(30)]
    public List<Guid>? SkillIds { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SkillIds is null) yield break;

        if (SkillIds.Contains(Guid.Empty))
        {
            yield return new ValidationResult(
                "Each skill ID must be a non-empty UUID.",
                new[] { nameof(SkillIds) });
        }
    }
}

public sealed class OnboardingStep3Request : IValidatableObject
{
    [Required]
    public string? PreferredRole { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PreferredRole is not null &&
            !OnboardingAllowedRoles.Names.Any(x => string.Equals(x, PreferredRole.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            yield return new ValidationResult("The preferred role is not in the list of supported roles.", new[] { nameof(PreferredRole) });
        }
    }
}

public static class OnboardingAllowedRoles
{
    public static readonly IReadOnlyList<string> Names =
    [
        "Backend Developer",
        "Frontend Developer",
        "Full-stack Developer",
        "AI Engineer",
        "Cloud Engineer",
        "UI/UX Designer"
    ];
}

public sealed class OnboardingStep4Request
{
    [Required]
    public string? ProfessionalInterests { get; set; }

    [Required]
    public string? LearningGoals { get; set; }
}

public sealed class OnboardingStep5Request
{
    [Required]
    [Range(1, 40)]
    public int? AvailableHoursPerWeek { get; set; }
}

public sealed class OnboardingStep6Request
{
    [Required]
    public string? PreviousProjectsAndContribution { get; set; }

    [Required]
    public string? TechnologiesToLearn { get; set; }
}

public sealed class OnboardingStep7Request : IValidatableObject
{
    [StringLength(1000)]
    public string? GithubUrl { get; set; }

    [StringLength(1000)]
    public string? PersonalWebsiteUrl { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!IsWebUrlOrEmpty(GithubUrl))
            yield return new ValidationResult("GitHub URL must start with http:// or https://.", new[] { nameof(GithubUrl) });
        if (!IsWebUrlOrEmpty(PersonalWebsiteUrl))
            yield return new ValidationResult("Portfolio URL must start with http:// or https://.", new[] { nameof(PersonalWebsiteUrl) });
    }

    public static bool IsWebUrlOrEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
         (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
}


public sealed class OnboardingCompleteRequest : IValidatableObject
{
    [Required]
    public bool? Confirmed { get; set; }

    // Optional: user-edited or AI-suggested summary.
    private string? _bio;

    [StringLength(2000)]
    public string? Bio
    {
        get => _bio;
        set
        {
            _bio = value;
            BioWasProvided = true;
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool BioWasProvided { get; private set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Confirmed == false)
        {
            yield return new ValidationResult("You must confirm profile completion.", new[] { nameof(Confirmed) });
        }
    }
}

