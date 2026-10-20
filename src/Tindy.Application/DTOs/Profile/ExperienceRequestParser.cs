using System.Globalization;
using System.Text.Json;
using Tindy.Application.Exceptions;

namespace Tindy.Application.DTOs.Profile;

// Strict JSON boundary shared by POST and full-replacement PUT in STEP 08.
public static class ExperienceRequestParser
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "project_name", "role_title", "description", "repo_url",
        "start_date", "end_date", "is_ongoing", "skill_ids"
    };

    public static ExperienceUpsertRequest Parse(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
            throw new ProfileValidationException(new Dictionary<string, string[]> { ["body"] = ["The request body must be a JSON object."] });

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in body.EnumerateObject())
        {
            if (!Allowed.Contains(property.Name))
                errors[property.Name] = ["This field is not supported."];
            else if (!fields.TryAdd(property.Name, property.Value))
                errors[property.Name] = ["Duplicate JSON properties are not allowed."];
        }

        string? Text(string name, bool required, int limit)
        {
            if (!fields.TryGetValue(name, out var element))
            {
                if (required) errors[name] = ["This field is required."];
                return null;
            }
            if (element.ValueKind != JsonValueKind.Null && element.ValueKind != JsonValueKind.String)
            {
                errors[name] = ["The field must be a string or null."];
                return null;
            }
            var value = element.ValueKind == JsonValueKind.Null ? null : element.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(value)) value = null;
            if (required && value is null) errors[name] = ["This field cannot be empty or null."];
            else if (value?.Length > limit) errors[name] = [$"The maximum length is {limit} characters."];
            return value;
        }

        DateOnly? Month(string name)
        {
            if (!fields.TryGetValue(name, out var element) || element.ValueKind == JsonValueKind.Null) return null;
            if (element.ValueKind != JsonValueKind.String || !TryParseMonth(element.GetString(), out var date))
            {
                errors[name] = ["The date must use YYYY-MM format."];
                return null;
            }
            return date;
        }

        var projectName = Text("project_name", true, 150);
        var roleTitle = Text("role_title", true, 150);
        var description = Text("description", true, 5000);
        var repoUrl = Text("repo_url", false, 1000);
        var startDate = Month("start_date");
        var endDate = Month("end_date");
        if (repoUrl is not null && !IsWebUrl(repoUrl))
            errors["repo_url"] = ["A valid absolute http:// or https:// URL is required."];

        bool ongoing = false;
        if (!fields.TryGetValue("is_ongoing", out var ongoingElement) ||
            (ongoingElement.ValueKind != JsonValueKind.True && ongoingElement.ValueKind != JsonValueKind.False))
            errors["is_ongoing"] = ["A boolean is required."];
        else ongoing = ongoingElement.GetBoolean();

        var ids = new List<Guid>();
        if (!fields.TryGetValue("skill_ids", out var skillsElement) || skillsElement.ValueKind != JsonValueKind.Array)
            errors["skill_ids"] = ["An array of up to 20 unique UUIDs is required (empty is allowed)."];
        else
        {
            foreach (var element in skillsElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String || !Guid.TryParse(element.GetString(), out var skillId) || skillId == Guid.Empty)
                {
                    errors["skill_ids"] = ["Every skill ID must be a non-empty UUID."];
                    break;
                }
                ids.Add(skillId);
            }
            if (ids.Count > 20 || ids.Count != ids.Distinct().Count())
                errors["skill_ids"] = ["Up to 20 unique skill IDs are allowed."];
        }

        if (ongoing && endDate is not null)
            errors["end_date"] = ["End date must be null for an ongoing experience."];
        if (startDate is not null && endDate is not null && endDate < startDate)
            errors["end_date"] = ["End date must not precede start date."];
        if (errors.Count != 0) throw new ProfileValidationException(errors);

        return new ExperienceUpsertRequest(projectName!, roleTitle!, description!, repoUrl,
            startDate, endDate, ongoing, ids.ToArray());
    }

    internal static bool TryParseMonth(string? input, out DateOnly result)
    {
        result = default;
        if (input is null || input.Length != 7 || input[4] != '-' ||
            !int.TryParse(input[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(input[5..], NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
            year is < 1 or > 9999 || month is < 1 or > 12) return false;
        result = new DateOnly(year, month, 1);
        return true;
    }

    internal static bool IsWebUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
        !string.IsNullOrWhiteSpace(uri.Host);
}

public static class ExperienceInputValidator
{
    // Defend the Application service against unvalidated commands bypassing HTTP.
    public static ExperienceUpsertRequest Normalize(ExperienceUpsertRequest? input)
    {
        if (input is null)
            throw new ProfileValidationException(new Dictionary<string, string[]> { ["body"] = ["An experience is required."] });
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var name = Clean(input.ProjectName);
        var role = Clean(input.RoleTitle);
        var description = Clean(input.Description);
        var repoUrl = Clean(input.RepoUrl);
        if (name is null || name.Length > 150) errors["project_name"] = ["Project name must contain 1 to 150 characters."];
        if (role is null || role.Length > 150) errors["role_title"] = ["Role title must contain 1 to 150 characters."];
        if (description is null || description.Length > 5000) errors["description"] = ["Description must contain 1 to 5000 characters."];
        if (repoUrl is not null && (repoUrl.Length > 1000 || !ExperienceRequestParser.IsWebUrl(repoUrl)))
            errors["repo_url"] = ["A valid http:// or https:// URL up to 1000 characters is required."];
        if (input.StartDate is not null && input.StartDate.Value.Day != 1)
            errors["start_date"] = ["Month-only dates must use the first day of the month internally."];
        if (input.EndDate is not null && input.EndDate.Value.Day != 1)
            errors["end_date"] = ["Month-only dates must use the first day of the month internally."];
        if (input.IsOngoing && input.EndDate is not null)
            errors["end_date"] = ["End date must be null for an ongoing experience."];
        if (input.StartDate is not null && input.EndDate is not null && input.EndDate < input.StartDate)
            errors["end_date"] = ["End date must not precede start date."];
        if (input.SkillIds is null || input.SkillIds.Count > 20 ||
            input.SkillIds.Any(id => id == Guid.Empty) || input.SkillIds.Distinct().Count() != input.SkillIds.Count)
            errors["skill_ids"] = ["Up to 20 unique, non-empty skill IDs are allowed; skill_ids is required."];
        if (errors.Count > 0) throw new ProfileValidationException(errors);
        return input with { ProjectName = name!, RoleTitle = role!, Description = description!, RepoUrl = repoUrl,
            SkillIds = input.SkillIds!.ToArray() };
    }
}
