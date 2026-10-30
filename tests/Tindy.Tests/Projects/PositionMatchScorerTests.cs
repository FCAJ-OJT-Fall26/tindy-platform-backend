using Tindy.Application.DTOs.Projects;
using Tindy.Application.Services;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class PositionMatchScorerTests
{
    private static readonly Guid Role = Guid.NewGuid(), Required1 = Guid.NewGuid(), Required2 = Guid.NewGuid(), Bonus = Guid.NewGuid();
    private static DiscoveryPosition Position() => new(Guid.NewGuid(), "Backend", Role, "Developer", "advanced", 2, 0, "open",
        [new(Required1, "C#", true), new(Required2, "SQL", true), new(Bonus, "Docker", false)]);

    [Theory]
    [InlineData("perfect", 100)]
    [InlineData("partial", 60)]
    [InlineData("empty_skills", 50)]
    [InlineData("wrong_role", 70)]
    [InlineData("no_required", 100)]
    [InlineData("no_optional", 100)]
    [InlineData("unknown_hours", 100)]
    [InlineData("no_groups_or_hours", 100)]
    [InlineData("no_member_data", 0)]
    public void ScoresAndNormalizesOnlyIncludedFactors(string scenario, int expected)
    {
        var position = Position();
        var member = new DiscoveryMemberProfile(Role, 10, [Required1, Required2, Bonus]);
        switch (scenario)
        {
            case "partial": member = member with { SkillIds = [Required1], AvailableHoursPerWeek = 5 }; break;
            case "empty_skills": member = member with { SkillIds = [] }; break;
            case "wrong_role": member = member with { PreferredRoleId = Guid.NewGuid() }; break;
            case "no_required": position = position with { Skills = position.Skills.Where(s => !s.IsRequired).ToArray() }; break;
            case "no_optional": position = position with { Skills = position.Skills.Where(s => s.IsRequired).ToArray() }; break;
            case "unknown_hours": member = member with { AvailableHoursPerWeek = null }; break;
            case "no_groups_or_hours": member = member with { AvailableHoursPerWeek = null }; position = position with { Skills = [] }; break;
            case "no_member_data": member = new(null, null, []); break;
        }
        var result = PositionMatchScorer.Score(member, position, 10);
        Assert.Equal(expected, result.MatchScore);
        Assert.Equal("rule-v1", result.AlgorithmVersion);
        Assert.InRange(result.MatchFactors.Sum(f => f.NormalizedWeight), 99.99999m, 100.00001m);
        if (!member.AvailableHoursPerWeek.HasValue)
        {
            Assert.True(result.DataIncomplete);
            Assert.Null(result.MatchFactors.Single(f => f.Name == "availability").Factor);
            Assert.Contains("excluded", result.Explanation);
        }
    }

    [Fact]
    public void Best_ExcludesClosedAndFilled_ThenBreaksTiesById()
    {
        var member = new DiscoveryMemberProfile(Role, 20, [Required1, Required2, Bonus]);
        var first = Position() with { PositionId = Guid.Parse("00000000-0000-0000-0000-000000000001") };
        var second = Position() with { PositionId = Guid.Parse("00000000-0000-0000-0000-000000000002") };
        Assert.Equal(first.PositionId, PositionMatchScorer.Best(member, [second, first], 10)!.BestPosition.PositionId);
        Assert.Equal(second.PositionId, PositionMatchScorer.Best(member, [first with { FilledCount = 2 }, second], 10)!.BestPosition.PositionId);
        Assert.Null(PositionMatchScorer.Best(member, [first with { Status = "closed" }], 10));
    }

    [Fact]
    public void SkillNamesAndExperienceLevel_DoNotInfluenceScore_AndRoundOnlyOnce()
    {
        var member = new DiscoveryMemberProfile(Role, 3, [Required1]);
        var position = Position();
        var result = PositionMatchScorer.Score(member, position, 7);
        Assert.Equal(59, result.MatchScore);
        Assert.Single(result.MatchedSkills);
        Assert.Equal(2, result.MissingSkills.Count);
        var changed = position with { ExperienceLevel = "beginner_friendly", Skills = position.Skills.Select(s => s with { Name = "same name" }).ToArray() };
        Assert.Equal(result.MatchScore, PositionMatchScorer.Score(member, changed, 7).MatchScore);
        Assert.Equal(50, PositionMatchScorer.Score(new(Role, 10, [Guid.NewGuid()]), position, 10).MatchScore);
    }
}
