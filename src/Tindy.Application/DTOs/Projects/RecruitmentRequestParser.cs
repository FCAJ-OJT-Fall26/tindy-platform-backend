using System.Text.Json;
using Tindy.Application.Exceptions;

namespace Tindy.Application.DTOs.Projects;

internal static class RecruitmentRequestParser
{
    internal static Dictionary<string, JsonElement> Object(JsonElement json, params string[] allowed)
    {
        if (json.ValueKind != JsonValueKind.Object) throw Invalid("body", "The request body must be a JSON object.");
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in json.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal)) throw Invalid(property.Name, "This field is not supported.");
            if (!result.TryAdd(property.Name, property.Value)) throw Invalid(property.Name, "Duplicate JSON properties are not allowed.");
        }
        return result;
    }

    internal static Guid Id(Dictionary<string, JsonElement> properties, string name)
    {
        if (!properties.TryGetValue(name, out var value) || value.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(value.GetString(), out var id) || id == Guid.Empty) throw Invalid(name, "A non-empty UUID is required.");
        return id;
    }

    internal static ProfileValidationException Invalid(string field, string message) => new(new Dictionary<string, string[]> { [field] = [message] });
}
