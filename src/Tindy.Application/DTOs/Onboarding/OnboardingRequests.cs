using System.ComponentModel.DataAnnotations;

namespace Tindy.Application.DTOs.Onboarding;

public sealed class OnboardingStep1Request
{
    [Required]
    [StringLength(50, MinimumLength = 3)]
    [RegularExpression(@"^[A-Za-z0-9_.]{3,50}$",
        ErrorMessage = "Username chỉ được chứa chữ Latin, số, dấu _ hoặc dấu . (3–50 ký tự).")]
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
    [Required]
    [MinLength(1)]
    public List<string>? Skills { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Skills is null) yield break;

        if (Skills.Any(s => string.IsNullOrWhiteSpace(s) || s.Trim().Length > 100))
        {
            yield return new ValidationResult(
                "Mỗi kỹ năng phải chứa 1–100 ký tự hợp lệ.",
                new[] { nameof(Skills) });
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
            !OnboardingAllowedRoles.Names.Any(x =>
                string.Equals(x, PreferredRole.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            yield return new ValidationResult(
                "Preferred role không nằm trong danh sách hỗ trợ.",
                new[] { nameof(PreferredRole) });
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
            yield return new ValidationResult("GitHub URL phải bắt đầu bằng http:// hoặc https://.",
                new[] { nameof(GithubUrl) });
        if (!IsWebUrlOrEmpty(PersonalWebsiteUrl))
            yield return new ValidationResult("Portfolio URL phải bắt đầu bằng http:// hoặc https://.",
                new[] { nameof(PersonalWebsiteUrl) });
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

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (Confirmed == false)
        {
            yield return new ValidationResult(
                "Phải xác nhận hoàn tất hồ sơ.",
                new[] { nameof(Confirmed) });
        }
    }
}

