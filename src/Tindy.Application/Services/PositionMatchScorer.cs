using Tindy.Application.DTOs.Projects;

namespace Tindy.Application.Services;

/// <summary>Pure rule-v1 scorer. Catalog IDs are the only comparison keys.</summary>
public static class PositionMatchScorer
{
    public static PositionMatch? Best(DiscoveryMemberProfile member, IEnumerable<DiscoveryPosition> positions, int expectedHours) =>
        positions.Where(p => p.Status == "open" && p.FilledCount < p.RequiredCount)
            .Select(p => Score(member, p, expectedHours))
            .OrderByDescending(m => m.MatchScore).ThenBy(m => m.BestPosition.PositionId).FirstOrDefault();

    // Historical applications are scored for the applied position even if it has
    // since closed/filled. Best() alone filters positions for new discovery.
    public static PositionMatch Score(DiscoveryMemberProfile member, DiscoveryPosition position, int expectedHours)
    {
        var skills = member.SkillIds.ToHashSet();
        var required = position.Skills.Where(s => s.IsRequired).DistinctBy(s => s.SkillId).ToArray();
        var optional = position.Skills.Where(s => !s.IsRequired).DistinctBy(s => s.SkillId).ToArray();
        var requiredMatched = required.Count(s => skills.Contains(s.SkillId));
        var optionalMatched = optional.Count(s => skills.Contains(s.SkillId));
        var role = member.PreferredRoleId.HasValue && member.PreferredRoleId == position.RoleId ? 1m : 0m;
        decimal? availability = member.AvailableHoursPerWeek is >= 0 && expectedHours > 0
            ? Math.Min(1m, (decimal)member.AvailableHoursPerWeek.Value / expectedHours) : null;
        (string Name, int Weight, decimal? Value)[] factors =
        [
            ("required_skills", 40, required.Length == 0 ? null : (decimal)requiredMatched / required.Length),
            ("preferred_role", 30, role),
            ("availability", 20, availability),
            ("optional_skills", 10, optional.Length == 0 ? null : (decimal)optionalMatched / optional.Length)
        ];
        var totalWeight = factors.Where(f => f.Value.HasValue).Sum(f => f.Weight);
        var breakdown = factors.Select(f => new MatchFactor(f.Name, f.Value,
            f.Value.HasValue ? 100m * f.Weight / totalWeight : 0m,
            f.Value.HasValue ? 100m * f.Weight * f.Value.Value / totalWeight : 0m)).ToArray();
        var score = (int)Math.Round(breakdown.Sum(f => f.Contribution), MidpointRounding.AwayFromZero);
        var all = required.Concat(optional).OrderBy(s => s.SkillId).ToArray();
        var explanation = $"Required skills: {requiredMatched}/{required.Length}; optional skills: {optionalMatched}/{optional.Length}. " +
            (member.PreferredRoleId.HasValue ? (role == 1 ? "Preferred role matches. " : "Preferred role differs. ") : "Preferred role is unknown. ") +
            (availability.HasValue
                ? $"Availability: {member.AvailableHoursPerWeek} hours/week; project expects {expectedHours}."
                : "Availability is unknown and excluded from scoring.");
        return new PositionMatch(Math.Clamp(score, 0, 100), "rule-v1",
            new(position.PositionId, position.Title, position.RoleId, position.RoleName, position.ExperienceLevel,
                position.RequiredCount, position.FilledCount, Math.Max(0, position.RequiredCount - position.FilledCount)),
            breakdown, all.Where(s => skills.Contains(s.SkillId)).ToArray(), all.Where(s => !skills.Contains(s.SkillId)).ToArray(),
            !member.PreferredRoleId.HasValue || !availability.HasValue || skills.Count == 0, explanation);
    }
}
