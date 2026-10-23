using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tindy.Application.Exceptions;

namespace Tindy.Application.DTOs.Ai;

public static class ProfileDraftValidator
{
    private static readonly HashSet<string> WarningCodes = new(StringComparer.Ordinal)
    {
        "CONFLICT", "INSUFFICIENT_EVIDENCE", "INCOMPLETE_DATE", "UNMATCHED_CATALOG",
        "MISSING_INFORMATION", "LIMIT_EXCEEDED"
    };

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };

    public static ProfileDraftResponse Parse(string json, IReadOnlySet<Guid> skills, IReadOnlySet<Guid> roles)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            CheckUniqueProperties(document.RootElement);
            var draft = JsonSerializer.Deserialize<ProfileDraftResponse>(json, Options);
            Require(draft is not null && draft.Profile is not null && draft.Experiences is not null &&
                draft.Certificates is not null && draft.Warnings is not null);
            var profile = draft!.Profile;
            CheckText(profile.DisplayName, 100);
            CheckText(profile.University, 255);
            CheckText(profile.PhoneNumber, 20);
            CheckText(profile.Headline, 255);
            CheckText(profile.Location, 255);
            CheckText(profile.CurrentRole, 255);
            Require(profile.PreferredRoleId is null || roles.Contains(profile.PreferredRoleId.Value));
            Require(profile.AvailableHoursPerWeek is null or >= 1 and <= 40);
            CheckText(profile.ProfessionalInterests, 5000);
            CheckText(profile.LearningGoals, 5000);
            CheckText(profile.PreviousProjectsAndContribution, 5000);
            CheckText(profile.TechnologiesToLearn, 5000);
            CheckUrl(profile.GithubUrl);
            CheckUrl(profile.LinkedinUrl);
            CheckUrl(profile.PersonalWebsiteUrl);
            CheckText(profile.PublicEmail, 255);
            Require(profile.PublicEmail is null || (MailAddress.TryCreate(profile.PublicEmail, out var email) &&
                string.Equals(email.Address, profile.PublicEmail, StringComparison.Ordinal)));
            CheckSkills(profile.SkillIds, 30, skills);

            Require(draft.Experiences.Count <= AiProfileLimits.MaxCollectionItems);
            foreach (var experience in draft.Experiences)
            {
                Require(experience is not null);
                CheckText(experience!.ProjectName, 150);
                CheckText(experience.RoleTitle, 150);
                CheckText(experience.Description, 5000);
                CheckUrl(experience.RepoUrl);
                var start = CheckDate(experience.StartDate, "yyyy-MM");
                var end = CheckDate(experience.EndDate, "yyyy-MM");
                Require(start is null || end is null || end >= start);
                Require(experience.IsOngoing != true || end is null);
                CheckSkills(experience.SkillIds, 20, skills);
                Require(HasEvidence(experience));
            }
            Require(draft.Certificates.Count <= AiProfileLimits.MaxCollectionItems);
            foreach (var certificate in draft.Certificates)
            {
                Require(certificate is not null);
                CheckText(certificate!.Title, 255);
                CheckText(certificate.Issuer, 255);
                CheckText(certificate.CredentialId, 255);
                CheckUrl(certificate.CredentialUrl);
                var issued = CheckDate(certificate.IssuedAt, "yyyy-MM-dd");
                var expires = CheckDate(certificate.ExpiresAt, "yyyy-MM-dd");
                Require(issued is null || expires is null || expires >= issued);
                Require(HasEvidence(certificate));
            }
            Require(draft.Warnings.Count <= AiProfileLimits.MaxWarnings);
            var fields = JsonSerializer.SerializeToElement(draft, Options);
            foreach (var warning in draft.Warnings)
            {
                Require(warning is not null && warning.Field is not null && warning.Code is not null && warning.Message is not null);
                CheckText(warning!.Field, 200);
                CheckText(warning.Code, 64);
                CheckText(warning.Message, 500);
                Require(WarningCodes.Contains(warning.Code));
                var target = ResolveWarningField(fields, warning.Field);
                // A group warning can describe an omitted disputed item. A named
                // scalar cannot simultaneously contain a suggestion and a conflict.
                Require(warning.Code != "CONFLICT" || target.ValueKind is
                    JsonValueKind.Null or JsonValueKind.Object or JsonValueKind.Array);
            }

            if (!HasEvidence(profile) && draft.Experiences.Count == 0 && draft.Certificates.Count == 0 &&
                !draft.Warnings.Any(x => x.Code == "INSUFFICIENT_EVIDENCE"))
            {
                // Keep the response bounded even if the provider filled every warning slot.
                draft = draft with
                {
                    Warnings = draft.Warnings.Take(AiProfileLimits.MaxWarnings - 1)
                    .Append(new ProfileDraftWarning("profile", "INSUFFICIENT_EVIDENCE",
                        "The source does not contain enough personal information to extract a profile.")).ToArray()
                };
            }
            return draft;
        }
        catch (JsonException)
        {
            throw new AiException(AiFailure.InvalidOutput);
        }
    }

    private static void CheckUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                Require(names.Add(property.Name));
                CheckUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) CheckUniqueProperties(item);
        }
    }

    private static void CheckText(string? value, int maxLength) =>
        Require(value is null || (!string.IsNullOrWhiteSpace(value) && value.Length <= maxLength &&
            !value.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t')));

    private static void CheckUrl(string? value)
    {
        CheckText(value, 1000);
        Require(value is null || (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme is "https" or "http" && !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo)));
    }

    private static DateOnly? CheckDate(string? value, string format)
    {
        if (value is null) return null;
        var parseValue = format == "yyyy-MM" ? value + "-01" : value;
        Require(value.Length == format.Length && DateOnly.TryParseExact(parseValue, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
        return DateOnly.ParseExact(parseValue, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static void CheckSkills(IReadOnlyList<Guid>? ids, int maximum, IReadOnlySet<Guid> skills) =>
        Require(ids is not null && ids.Count <= maximum && ids.Distinct().Count() == ids.Count && ids.All(skills.Contains));

    private static bool HasEvidence<T>(T value) =>
        JsonSerializer.SerializeToElement(value, Options).EnumerateObject().Any(property =>
            property.Value.ValueKind != JsonValueKind.Null &&
            (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() > 0));

    private static JsonElement ResolveWarningField(JsonElement root, string path)
    {
        if (path == "$") return root;
        var segments = path.Split('.');
        Require(segments[0] is "profile" or "experiences" or "certificates" ||
            segments[0].StartsWith("experiences[", StringComparison.Ordinal) ||
            segments[0].StartsWith("certificates[", StringComparison.Ordinal));
        var current = root;
        foreach (var segment in segments)
        {
            var bracket = segment.IndexOf('[');
            var name = bracket < 0 ? segment : segment[..bracket];
            Require(current.ValueKind == JsonValueKind.Object && current.TryGetProperty(name, out _));
            current = current.GetProperty(name);
            if (bracket < 0) continue;
            Require(segment.EndsWith(']') && current.ValueKind == JsonValueKind.Array);
            Require(int.TryParse(segment.AsSpan(bracket + 1, segment.Length - bracket - 2),
                NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
                index >= 0 && index < current.GetArrayLength());
            current = current[index];
        }
        return current;
    }

    private static void Require([DoesNotReturnIf(false)] bool condition)
    {
        if (!condition) throw new AiException(AiFailure.InvalidOutput);
    }
}
