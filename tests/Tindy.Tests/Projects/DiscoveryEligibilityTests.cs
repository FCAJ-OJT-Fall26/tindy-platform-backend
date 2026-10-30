using Tindy.Application.DTOs.Projects;
using Tindy.Application.Entities;
using Tindy.Infrastructure.Repositories;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class DiscoveryEligibilityTests
{
    [Theory]
    [InlineData("expired")]
    [InlineData("archived")]
    [InlineData("status")]
    [InlineData("full")]
    [InlineData("own")]
    [InlineData("joined")]
    [InlineData("closed_position")]
    [InlineData("filled_position")]
    [InlineData("pending")]
    public void IneligibleProject_IsExcluded(string reason)
    {
        var user = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var project = Candidate(now);
        switch (reason)
        {
            case "expired": project.RecruitmentClosesAt = now; break;
            case "archived": project.ArchivedAt = now; break;
            case "status": project.Status = "in_progress"; break;
            case "full": project.MaxTeamCapacity = 1; break;
            case "own": project.LeaderId = user; break;
            case "joined": project.Members.Add(new() { UserId = user }); break;
            case "closed_position": project.Positions.Single().Status = "closed"; break;
            case "filled_position": project.Positions.Single().FilledCount = 1; break;
            case "pending": project.Applications.Add(new() { ApplicantId = user }); break;
        }
        Assert.Empty(DiscoveryEligibility.ForMember(new[] { project }.AsQueryable(), user, now));
    }

    [Fact]
    public void OtherProjectLeader_AndFormerMember_AreEligibleWithoutSkillThreshold()
    {
        var user = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var own = Candidate(now); own.LeaderId = user;
        var other = Candidate(now); other.Members.Add(new() { UserId = user, Status = "left" });
        var result = DiscoveryEligibility.ForMember(new[] { own, other }.AsQueryable(), user, now).ToArray();
        Assert.Same(other, Assert.Single(result));
    }

    [Fact]
    public void CatalogFilters_OnlyMatchOpenAvailablePositions_UsingIds()
    {
        var project = Candidate(DateTime.UtcNow);
        var position = project.Positions.Single();
        position.RoleId = Guid.NewGuid();
        var skill = Guid.NewGuid();
        position.PositionSkills.Add(new() { SkillId = skill });
        var source = new[] { project }.AsQueryable();
        Assert.Single(DiscoveryEligibility.Filter(source, new("Web", position.RoleId, skill)));
        Assert.Empty(DiscoveryEligibility.Filter(source, new(RoleId: Guid.NewGuid())));
        Assert.Empty(DiscoveryEligibility.Filter(source, new(SkillId: Guid.NewGuid())));
        position.Status = "closed";
        Assert.Empty(DiscoveryEligibility.Filter(source, new(RoleId: position.RoleId)));
    }

    private static Project Candidate(DateTime now) => new()
    {
        LeaderId = Guid.NewGuid(), Category = "Web", MaxTeamCapacity = 5, RecruitmentClosesAt = now.AddDays(1),
        Members = [new() { UserId = Guid.NewGuid(), IsLeader = true }],
        Positions = [new() { RequiredCount = 1 }]
    };
}
