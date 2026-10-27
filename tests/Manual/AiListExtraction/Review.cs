using System.Globalization;
using System.Text;
using Tindy.Application.DTOs.Ai;

namespace Tindy.Manual.AiListExtraction;

internal sealed record ExpectedExperience(string Employer, string Role, string StartDate, string? EndDate, bool IsOngoing);
internal sealed record ExpectedCertificate(string Title);
internal sealed record Expectations(string[] RequiredSkills, string[] ForbiddenSkills,
    ExpectedExperience[] Experiences, ExpectedCertificate[] Certificates);

internal static class Review
{
    public static IReadOnlyList<string> Check(ProfileDraftResponse draft, IReadOnlyDictionary<Guid, string> catalog, Expectations expected)
    {
        var failures = new List<string>();
        var skills = draft.Profile.SkillIds.Select(id => catalog.GetValueOrDefault(id, "UNKNOWN_ID")).ToArray();
        if (skills.Length != expected.RequiredSkills.Length) failures.Add("The extracted skill set differs from the reference expectations.");
        foreach (var name in expected.RequiredSkills)
            if (!skills.Any(skill => Equal(skill, name))) failures.Add("Missing expected skill: " + name);
        foreach (var name in expected.ForbiddenSkills)
            if (skills.Any(skill => Equal(skill, name))) failures.Add("Unsupported inferred skill: " + name);
        if (draft.Experiences.Count != expected.Experiences.Length) failures.Add("Experience count mismatch.");
        foreach (var item in expected.Experiences)
        {
            var matches = draft.Experiences.Where(value => Contains(value.Description, item.Employer)).ToArray();
            if (matches.Length != 1) { failures.Add("Missing or duplicate employment entry: " + item.Employer); continue; }
            var value = matches[0];
            if (!Equal(value.RoleTitle, item.Role) || value.StartDate != item.StartDate || value.EndDate != item.EndDate || value.IsOngoing != item.IsOngoing)
                failures.Add($"Employment role/date mismatch: {item.Employer}; role='{value.RoleTitle}', start='{value.StartDate}', end='{value.EndDate}', ongoing={value.IsOngoing}; expected role='{item.Role}', start='{item.StartDate}', end='{item.EndDate}', ongoing={item.IsOngoing}");
            if (value.ProjectName is not null) failures.Add("Invented project name for employment entry: " + item.Employer);
        }
        if (draft.Certificates.Count != expected.Certificates.Length) failures.Add("Certificate count mismatch.");
        foreach (var item in expected.Certificates)
        {
            var matches = draft.Certificates.Where(value => Contains(value.Title, item.Title)).ToArray();
            if (matches.Length != 1) { failures.Add("Missing or duplicate certificate: " + item.Title); continue; }
            if (matches[0].IssuedAt is not null || matches[0].ExpiresAt is not null)
                failures.Add("Fabricated complete certificate date: " + item.Title);
        }
        if (draft.Profile.SkillIds.Distinct().Count() != draft.Profile.SkillIds.Count || draft.Profile.SkillIds.Any(id => !catalog.ContainsKey(id)))
            failures.Add("Invalid or duplicate overall skill UUIDs.");
        if (draft.Experiences.Any(item => item.SkillIds.Any(id => expected.ForbiddenSkills.Any(name => Equal(catalog.GetValueOrDefault(id), name)))))
            failures.Add("Unsupported inferred skill in an experience.");
        if (draft.Experiences.Any(item => item.SkillIds.Distinct().Count() != item.SkillIds.Count || item.SkillIds.Any(id => !catalog.ContainsKey(id))))
            failures.Add("Invalid or duplicate experience skill UUIDs.");
        return failures;
    }

    private static bool Equal(string? first, string second) => Normalize(first) == Normalize(second);
    private static bool Contains(string? first, string second) => Normalize(first).Contains(Normalize(second), StringComparison.Ordinal);
    private static string Normalize(string? value) => string.Concat((value ?? "").Normalize(NormalizationForm.FormD)
        .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character))).ToUpperInvariant();
}
