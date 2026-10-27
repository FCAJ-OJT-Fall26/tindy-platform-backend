using System.Text.Json.Nodes;
using Tindy.Application.Exceptions;

namespace Tindy.Application.DTOs.Ai;

public static class AiExperienceRoleResolver
{
    // The model selects a request-local reference. Only the server supplies the public title.
    public static void Normalize(JsonObject experience, AiRoleReferenceMap roles, JsonArray warnings, int index)
    {
        var key = experience["role_key"]?.GetValue<string>();
        var source = experience["role_title_source"]?.GetValue<string>();
        if (source is not null && (string.IsNullOrWhiteSpace(source) || source.Length > 150 || source.Any(char.IsControl)))
            throw Invalid();
        var id = roles.Resolve(key);
        if (id is not null && source is null) throw Invalid();
        var title = id is null ? null : roles.References.Single(role => role.Key == key).Name;
        if (source is not null && !string.Equals(source.Trim(), title, StringComparison.OrdinalIgnoreCase))
        {
            var description = experience["description"]?.GetValue<string>();
            experience["description"] = source.Trim() + (string.IsNullOrWhiteSpace(description) ? "" : ": " + description);
        }
        if (source is not null && title is null)
        {
            if (warnings.Count >= AiProfileLimits.MaxWarnings) throw Invalid();
            warnings.Add(new JsonObject { ["field"] = $"experiences[{index}].role_title", ["code"] = "UNMATCHED_CATALOG",
                ["message"] = "The source role has no selected active catalog equivalent; its original title was preserved in the description." });
        }
        experience.Remove("role_key");
        experience.Remove("role_title_source");
        experience["role_title"] = title;
    }

    private static AiException Invalid() => new(AiFailure.InvalidOutput) { ValidationReason = "invalid_experience_role_reference" };
}
