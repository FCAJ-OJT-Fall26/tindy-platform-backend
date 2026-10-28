using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Tindy.Application.Exceptions;

namespace Tindy.Application.DTOs.Ai;

public static class AiScalarProfileDraftValidator
{
    public static readonly IReadOnlySet<string> ScalarFields = System.Collections.Frozen.FrozenSet.ToFrozenSet(
        typeof(ProfileDraft).GetProperties().Where(property => property.Name is not (nameof(ProfileDraft.SkillIds) or nameof(ProfileDraft.PreferredRoleId)))
            .Select(property => JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name)), StringComparer.Ordinal);

    public static ProfileDraftResponse Parse(string json, AiRoleReferenceMap roles)
    {
        try
        {
            Require(Encoding.UTF8.GetByteCount(json) <= AiProfileLimits.MaxOutputBytes);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            ProfileDraftValidator.CheckUniqueProperties(document.RootElement);
            var root = JsonNode.Parse(json) as JsonObject;
            Require(root is not null && root.Count == 2 && root["profile"] is JsonObject && root["warnings"] is JsonArray);
            var profile = (JsonObject)root!["profile"]!;
            Require(profile.All(field => ScalarFields.Contains(field.Key) || field.Key is "preferred_role_key" or "preferred_role_statement"));
            var warnings = (JsonArray)root["warnings"]!;
            foreach (var node in warnings)
            {
                Require(node is JsonObject && node["field"] is JsonValue);
                var field = node!["field"]!.GetValue<string>();
                if (field is "profile.preferred_role_key" or "profile.preferred_role_statement") field = "profile.preferred_role_id";
                Require(field is "$" or "profile" or "profile.preferred_role_id" ||
                    (field.StartsWith("profile.", StringComparison.Ordinal) && ScalarFields.Contains(field[8..])));
                node["field"] = field;
            }
            var key = profile["preferred_role_key"]?.GetValue<string>();
            var statement = profile["preferred_role_statement"]?.GetValue<string>();
            Require(statement is null || statement.Length <= 500);
            var roleId = roles.Resolve(key);
            if (roleId is not null)
            {
                var name = roles.References.Single(role => role.Key == key).Name;
                var explicitPreference = statement is not null &&
                    !Regex.IsMatch(statement, @"\b(not|never|don't|do not)\b|không", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
                    Regex.Matches(statement, @"(?:\b(?:want|seek|seeking|prefer|looking for|apply for|interested in)\b|mong muốn|muốn làm|ứng tuyển|vị trí mong muốn|vai trò mong muốn)([^.!?;\n]{1,200})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                        .Any(match => AiSkillEvidence.Matches(name, match.Groups[1].Value));
                if (!explicitPreference)
                {
                    roleId = null;
                    AddWarning(warnings, "INSUFFICIENT_EVIDENCE", "An explicit preferred-role statement was not established; current role was not used as a preference.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(statement))
                AddWarning(warnings, "UNMATCHED_CATALOG", "The stated preferred role could not be matched to the active role catalog.");
            profile.Remove("preferred_role_key");
            profile.Remove("preferred_role_statement");
            profile["preferred_role_id"] = roleId is null ? null : JsonValue.Create(roleId.Value);
            profile["skill_ids"] = new JsonArray();
            var normalized = new JsonObject { ["profile"] = profile.DeepClone(), ["experiences"] = new JsonArray(),
                ["certificates"] = new JsonArray(), ["warnings"] = warnings.DeepClone() };
            return ProfileDraftValidator.Parse(normalized.ToJsonString(), new HashSet<Guid>(), roles.ActiveIds);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException || error is AiException { Failure: AiFailure.InvalidOutput })
        {
            throw new AiException(AiFailure.InvalidOutput, json, "tool_input")
            { ValidationReason = (error as AiException)?.ValidationReason ?? "invalid_scalar_profile" };
        }
    }

    private static void AddWarning(JsonArray warnings, string code, string message)
    {
        Require(warnings.Count < AiProfileLimits.MaxWarnings);
        warnings.Add(new JsonObject { ["field"] = "profile.preferred_role_id", ["code"] = code, ["message"] = message });
    }
    private static void Require(bool condition)
    {
        if (!condition) throw new AiException(AiFailure.InvalidOutput);
    }
}
