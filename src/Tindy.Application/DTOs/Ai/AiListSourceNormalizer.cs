using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Tindy.Application.Exceptions;

namespace Tindy.Application.DTOs.Ai;

// Model copies source dates; the server, not the model, decides which precision is supported.
internal static class AiListSourceNormalizer
{
    public static void Normalize(JsonArray experiences, JsonArray certificates, JsonArray warnings)
    {
        for (var index = 0; index < experiences.Count; index++)
        {
            var item = experiences[index] as JsonObject ?? throw Invalid();
            AllowOnly(item, ["employer_name", "project_name", "role_key", "role_title_source", "description", "repo_url", "start_date_source", "end_date_source", "skill_ids"]);
            var employer = Read(item, "employer_name", 255);
            if (employer is not null)
            {
                var description = Read(item, "description", 1200);
                item["description"] = employer + (description is null ? "" : ": " + description);
            }
            var start = Read(item, "start_date_source", 100);
            var end = Read(item, "end_date_source", 100);
            var ongoing = IsOngoing(end);
            item["start_date"] = Month(start, $"experiences[{index}].start_date", warnings);
            item["end_date"] = ongoing ? null : Month(end, $"experiences[{index}].end_date", warnings);
            item["is_ongoing"] = ongoing ? true : end is not null && item["end_date"] is not null ? false : null;
            item.Remove("employer_name");
            item.Remove("start_date_source");
            item.Remove("end_date_source");
        }
        for (var index = 0; index < certificates.Count; index++)
        {
            var item = certificates[index] as JsonObject ?? throw Invalid();
            AllowOnly(item, ["title", "issuer", "credential_id", "credential_url", "issued_at_source", "expires_at_source"]);
            item["issued_at"] = Day(Read(item, "issued_at_source", 100), $"certificates[{index}].issued_at", warnings);
            item["expires_at"] = Day(Read(item, "expires_at_source", 100), $"certificates[{index}].expires_at", warnings);
            item.Remove("issued_at_source");
            item.Remove("expires_at_source");
        }
    }

    private static string? Month(string? source, string field, JsonArray warnings)
    {
        if (source is null) return null;
        var match = Regex.Match(source, @"\A(?:(?<year>\d{4})[-/](?<month>\d{1,2})|(?<month>\d{1,2})[-/](?<year>\d{4}))\z", RegexOptions.CultureInvariant);
        if (match.Success && int.TryParse(match.Groups["year"].Value, out var year) && int.TryParse(match.Groups["month"].Value, out var month) && year is >= 1 and <= 9999 && month is >= 1 and <= 12)
            return new DateOnly(year, month, 1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        if (TryDay(source, out var date)) return date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        Warn(warnings, field, source);
        return null;
    }

    private static string? Day(string? source, string field, JsonArray warnings)
    {
        if (source is null) return null;
        if (TryDay(source, out var date)) return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Warn(warnings, field, source);
        return null;
    }

    private static bool TryDay(string source, out DateOnly date)
    {
        // Year-first dates are unambiguous. Day-first dates require an unambiguous day (>12),
        // or identical day/month. Otherwise preserve uncertainty instead of guessing locale.
        if (DateOnly.TryParseExact(source, ["yyyy-MM-dd", "yyyy/MM/dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        if (DateOnly.TryParseExact(source, ["dd/MM/yyyy", "dd-MM-yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date) && (date.Day > 12 || date.Day == date.Month)) return true;
        date = default;
        return false;
    }

    private static bool IsOngoing(string? source) => source?.Trim().ToLowerInvariant() is "nay" or "hiện tại" or "đến nay" or "present" or "current" or "ongoing" or "now";
    private static string? Read(JsonObject item, string key, int maximum)
    {
        if (item[key] is null) return null;
        var value = item[key]!.GetValue<string>();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t')) throw Invalid();
        return value.Trim();
    }
    private static void AllowOnly(JsonObject item, string[] allowed)
    {
        if (item.Any(field => !allowed.Contains(field.Key, StringComparer.Ordinal))) throw Invalid();
    }
    private static void Warn(JsonArray warnings, string field, string source)
    {
        var message = $"Source date '{source}' has insufficient or ambiguous components; no date was inferred.";
        var existing = warnings.FirstOrDefault(value => value is JsonObject && value["field"]?.GetValue<string>() == field && value["code"]?.GetValue<string>() == "INCOMPLETE_DATE");
        if (existing is not null)
        {
            existing["message"] = message;
            return;
        }
        if (warnings.Count >= AiProfileLimits.MaxWarnings) throw Invalid();
        warnings.Add(new JsonObject { ["field"] = field, ["code"] = "INCOMPLETE_DATE", ["message"] = message });
    }
    private static AiException Invalid() => new(AiFailure.InvalidOutput) { ValidationReason = "invalid_list_source_fields" };
}
