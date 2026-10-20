using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Tindy.Application.Exceptions;

namespace Tindy.Application.DTOs.Profile;

// Default(OptionalField<T>) means a JSON property was NOT supplied.
// Specified(null) is intentional clearing of an optional property.
public readonly record struct OptionalField<T>(bool IsSpecified, T? Value)
{
    public static OptionalField<T> Specified(T? value) => new(true, value);
}

public sealed record UpdateProfileRequest
{
    public OptionalField<string?> DisplayName { get; init; }
    public OptionalField<string?> University { get; init; }
    public OptionalField<string?> PhoneNumber { get; init; }
    public OptionalField<string?> Headline { get; init; }
    public OptionalField<string?> Bio { get; init; }
    public OptionalField<string?> Location { get; init; }
    public OptionalField<string?> CurrentRole { get; init; }
    public OptionalField<Guid?> PreferredRoleId { get; init; }
    public OptionalField<int?> AvailableHoursPerWeek { get; init; }
    public OptionalField<string?> ProfessionalInterests { get; init; }
    public OptionalField<string?> LearningGoals { get; init; }
    public OptionalField<string?> PreviousProjectsAndContribution { get; init; }
    public OptionalField<string?> TechnologiesToLearn { get; init; }
    public OptionalField<string?> GithubUrl { get; init; }
    public OptionalField<string?> LinkedinUrl { get; init; }
    public OptionalField<string?> PersonalWebsiteUrl { get; init; }
    public OptionalField<string?> ResumeUrl { get; init; }
    public OptionalField<string?> PublicEmail { get; init; }
    public OptionalField<IReadOnlyList<Guid>?> SkillIds { get; init; }
}

// Strict, presence-aware JSON parser. Kept in Application so it can be unit-tested
// without ASP.NET, a running database or Docker. No Account/Identity fields allowed.
public static class ProfilePatchParser
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "display_name", "university", "phone_number", "headline", "bio",
        "location", "current_role", "preferred_role_id", "available_hours_per_week",
        "professional_interests", "learning_goals", "previous_projects_and_contribution",
        "technologies_to_learn", "github_url", "linkedin_url", "personal_website_url",
        "resume_url", "public_email", "skill_ids"
    };

    public static UpdateProfileRequest Parse(JsonElement json)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (json.ValueKind != JsonValueKind.Object)
            throw new ProfileValidationException(new Dictionary<string, string[]> { ["body"] = ["The request body must be a JSON object."] });

        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in json.EnumerateObject())
        {
            if (!Allowed.Contains(property.Name))
            {
                errors[property.Name] = ["This field cannot be updated through the profile API."];
                continue;
            }
            if (!properties.TryAdd(property.Name, property.Value))
                errors[property.Name] = ["Duplicate JSON properties are not allowed."];
        }
        if (properties.Count == 0 && errors.Count == 0)
            errors["body"] = ["At least one profile field must be provided."];

        OptionalField<string?> Text(string name, int maxLength, bool required = false, bool webUrl = false, bool email = false)
        {
            if (!properties.TryGetValue(name, out var value)) return default;
            if (value.ValueKind != JsonValueKind.Null && value.ValueKind != JsonValueKind.String)
            {
                errors[name] = ["The field must be a string or null."];
                return default;
            }
            var text = value.ValueKind == JsonValueKind.Null ? null : value.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(text)) text = null;
            if (required && text is null)
                errors[name] = ["This field cannot be empty or null."];
            else if (text?.Length > maxLength)
                errors[name] = [$"The maximum length is {maxLength} characters."];
            else if (webUrl && text is not null && (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
                     (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
                     string.IsNullOrEmpty(uri.Host)))
                errors[name] = ["A valid absolute http:// or https:// URL is required."];
            else if (email && text is not null && !new EmailAddressAttribute().IsValid(text))
                errors[name] = ["A valid email address is required."];
            return OptionalField<string?>.Specified(text);
        }

        OptionalField<Guid?> RoleId()
        {
            const string name = "preferred_role_id";
            if (!properties.TryGetValue(name, out var value)) return default;
            if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out var id) || id == Guid.Empty)
            {
                errors[name] = ["A non-empty UUID is required."];
                return default;
            }
            return OptionalField<Guid?>.Specified(id);
        }

        OptionalField<int?> Hours()
        {
            const string name = "available_hours_per_week";
            if (!properties.TryGetValue(name, out var value)) return default;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var hours) || hours is < 1 or > 40)
            {
                errors[name] = ["Available hours per week must be an integer between 1 and 40."];
                return default;
            }
            return OptionalField<int?>.Specified(hours);
        }

        OptionalField<IReadOnlyList<Guid>?> Skills()
        {
            const string name = "skill_ids";
            if (!properties.TryGetValue(name, out var value)) return default;
            if (value.ValueKind != JsonValueKind.Array)
            {
                errors[name] = ["Skill IDs must be an array of 1 to 30 unique UUIDs."];
                return default;
            }
            var ids = new List<Guid>();
            foreach (var element in value.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String || !Guid.TryParse(element.GetString(), out var id) || id == Guid.Empty)
                {
                    errors[name] = ["Every skill ID must be a non-empty UUID."];
                    return default;
                }
                ids.Add(id);
            }
            if (ids.Count is < 1 or > 30 || ids.Distinct().Count() != ids.Count)
            {
                errors[name] = ["Select 1 to 30 unique skills."];
                return default;
            }
            return OptionalField<IReadOnlyList<Guid>?>.Specified(ids);
        }

        var request = new UpdateProfileRequest
        {
            DisplayName = Text("display_name", 100, required: true),
            University = Text("university", 255, required: true),
            PhoneNumber = Text("phone_number", 20),
            Headline = Text("headline", 255),
            Bio = Text("bio", 2000),
            Location = Text("location", 255),
            CurrentRole = Text("current_role", 255),
            PreferredRoleId = RoleId(),
            AvailableHoursPerWeek = Hours(),
            ProfessionalInterests = Text("professional_interests", 5000),
            LearningGoals = Text("learning_goals", 5000),
            PreviousProjectsAndContribution = Text("previous_projects_and_contribution", 5000),
            TechnologiesToLearn = Text("technologies_to_learn", 5000),
            GithubUrl = Text("github_url", 1000, webUrl: true),
            LinkedinUrl = Text("linkedin_url", 1000, webUrl: true),
            PersonalWebsiteUrl = Text("personal_website_url", 1000, webUrl: true),
            ResumeUrl = Text("resume_url", 1000, webUrl: true),
            PublicEmail = Text("public_email", 255, email: true),
            SkillIds = Skills()
        };

        if (errors.Count != 0) throw new ProfileValidationException(errors);
        return request;
    }
}
