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
            var warnings = (JsonArray)root!["warnings"]!;
            foreach (var warning in warnings)
            {
                Require(warning is JsonObject && warning["field"] is JsonValue);
                var field = warning!["field"]!.GetValue<string>();
                Require(field == "$" || field == "skill_ids" || field.StartsWith("skill_ids[", StringComparison.Ordinal) ||
                    field == "experiences" || field.StartsWith("experiences[", StringComparison.Ordinal) ||
                    field == "certificates" || field.StartsWith("certificates[", StringComparison.Ordinal));
                field = field.Replace(".start_date_source", ".start_date", StringComparison.Ordinal)
                    .Replace(".end_date_source", ".end_date", StringComparison.Ordinal)
                    .Replace(".issued_at_source", ".issued_at", StringComparison.Ordinal)
                    .Replace(".expires_at_source", ".expires_at", StringComparison.Ordinal)
                    .Replace(".employer_name", ".description", StringComparison.Ordinal);
                warning["field"] = field;
                if (field == "skill_ids" || field.StartsWith("skill_ids[", StringComparison.Ordinal))
                    warning["field"] = "profile." + field;
            }
            var ids = Resolve(root["skill_ids"], references, 30, warnings, "profile.skill_ids");
            var experiences = (JsonArray)root["experiences"]!;
            Require(experiences.Count <= AiProfileLimits.MaxCollectionItems);
            for (var index = 0; index < experiences.Count; index++)
            {
                var node = experiences[index];
                Require(node is JsonObject);
                node!["skill_ids"] = Resolve(node["skill_ids"], references, 20, warnings, $"experiences[{index}].skill_ids");
            }
            AiListSourceNormalizer.Normalize(experiences, (JsonArray)root["certificates"]!, warnings);
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

    private static JsonArray Resolve(JsonNode? node, AiSkillReferenceMap references, int maximum, JsonArray warnings, string field)
    {
        Require(node is JsonArray);
        var items = (JsonArray)node!;
        var keys = new List<string>();
        var quotes = new List<string>();
        foreach (var item in items)
        {
            Require(item is JsonObject match && match.Count == 2 && match.ContainsKey("key") && match.ContainsKey("evidence"));
            var key = item!["key"]?.GetValue<string>();
            var quote = item["evidence"]?.GetValue<string>();
            Require(key is not null && !string.IsNullOrWhiteSpace(quote) && quote.Length <= 200);
            keys.Add(key!);
            quotes.Add(quote!);
        }
        // Unknown/duplicate/excess keys are errors, never silently repaired by evidence filtering.
        var ids = references.Resolve(keys, maximum);
        var names = references.References.ToDictionary(reference => reference.Key, reference => reference.Name, StringComparer.Ordinal);
        var accepted = new JsonArray();
        var omitted = false;
        for (var index = 0; index < keys.Count; index++)
        {
            if (AiSkillEvidence.Matches(names[keys[index]], quotes[index])) accepted.Add(JsonValue.Create(ids[index]));
            else omitted = true;
        }
        if (omitted)
        {
            Require(warnings.Count < AiProfileLimits.MaxWarnings);
            warnings.Add(new JsonObject { ["field"] = field, ["code"] = "INSUFFICIENT_EVIDENCE",
                ["message"] = "A suggested skill lacked matching explicit evidence and was omitted." });
        }
        return accepted;
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new AiException(AiFailure.InvalidOutput);
    }
}
