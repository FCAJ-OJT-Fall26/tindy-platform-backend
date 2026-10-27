using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tindy.Application.Exceptions;

namespace Tindy.Application.DTOs.Ai;

// Internal tool shape is list-only. The public shape remains ProfileDraftResponse.
public static class AiListDraftValidator
{
    public static ProfileDraftResponse Parse(string json, AiSkillReferenceMap references)
    {
        try
        {
            Require(Encoding.UTF8.GetByteCount(json) <= AiProfileLimits.MaxOutputBytes);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            ProfileDraftValidator.CheckUniqueProperties(document.RootElement);
            var root = JsonNode.Parse(json) as JsonObject;
            Require(root is not null && root.Count == 4 &&
                root.ContainsKey("skill_ids") && root["experiences"] is JsonArray &&
                root["certificates"] is JsonArray && root["warnings"] is JsonArray);
            var ids = Resolve(root!["skill_ids"], references, 30);
            var experiences = (JsonArray)root["experiences"]!;
            Require(experiences.Count <= AiProfileLimits.MaxCollectionItems);
            foreach (var node in experiences)
            {
                Require(node is JsonObject);
                node!["skill_ids"] = Resolve(node["skill_ids"], references, 20);
            }
            var warnings = (JsonArray)root["warnings"]!;
            foreach (var warning in warnings)
            {
                Require(warning is JsonObject && warning["field"] is JsonValue);
                var field = warning!["field"]!.GetValue<string>();
                Require(field == "$" || field == "skill_ids" || field.StartsWith("skill_ids[", StringComparison.Ordinal) ||
                    field == "experiences" || field.StartsWith("experiences[", StringComparison.Ordinal) ||
                    field == "certificates" || field.StartsWith("certificates[", StringComparison.Ordinal));
                if (field == "skill_ids" || field.StartsWith("skill_ids[", StringComparison.Ordinal))
                    warning["field"] = "profile." + field;
            }
            var normalized = new JsonObject
            {
                ["profile"] = new JsonObject { ["skill_ids"] = ids },
                ["experiences"] = experiences.DeepClone(),
                ["certificates"] = root["certificates"]!.DeepClone(),
                ["warnings"] = warnings.DeepClone()
            };
            // Reuse every existing scalar, date, collection, warning and active-UUID rule.
            var activeIds = references.Resolve(references.References.Select(item => item.Key).ToArray(), int.MaxValue).ToHashSet();
            return ProfileDraftValidator.Parse(normalized.ToJsonString(), activeIds, new HashSet<Guid>());
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException ||
            error is AiException { Failure: AiFailure.InvalidOutput })
        {
            throw new AiException(AiFailure.InvalidOutput, json, "tool_input")
            { ValidationReason = (error as AiException)?.ValidationReason ?? "invalid_list_schema" };
        }
    }

    private static JsonArray Resolve(JsonNode? node, AiSkillReferenceMap references, int maximum)
    {
        Require(node is JsonArray);
        var keys = ((JsonArray)node!).Select(value => value?.GetValue<string>()!).ToArray();
        return new JsonArray(references.Resolve(keys, maximum)
            .Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new AiException(AiFailure.InvalidOutput);
    }
}
