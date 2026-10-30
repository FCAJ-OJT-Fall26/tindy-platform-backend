using Microsoft.EntityFrameworkCore;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Entities;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

public sealed class DiscoveryRepository(TindyDbContext db) : IDiscoveryRepository
{
    public async Task<DiscoveryMemberProfile> GetMemberAsync(Guid userId, CancellationToken ct = default) =>
        await db.UserProfiles.AsNoTracking().Where(p => p.UserId == userId)
            .Select(p => new DiscoveryMemberProfile(p.PreferredRoleId, p.AvailableHoursPerWeek,
                p.UserSkills.Select(s => s.SkillId).ToList())).SingleOrDefaultAsync(ct)
        ?? new DiscoveryMemberProfile(null, null, []);

    public async Task<IReadOnlyList<DiscoveryProject>> GetCandidatesAsync(Guid userId, DateTime now, DateTime snapshot,
        DiscoveryFilters filters, CancellationToken ct = default)
    {
        var query = DiscoveryEligibility.Filter(DiscoveryEligibility.ForMember(db.Projects.AsNoTracking(), userId, now), filters)
            .Where(p => p.CreatedAt <= snapshot && !p.DiscoveryInteractions.Any(i => i.UserId == userId && i.IsSkipped));
        return await ProjectCards(query.OrderBy(p => p.ProjectId), userId).ToListAsync(ct);
    }

    internal static IQueryable<DiscoveryProject> ProjectCards(IQueryable<Project> projects, Guid userId) => projects.AsSplitQuery().Select(p =>
        new DiscoveryProject(p.ProjectId, p.Name, p.Tagline, p.Description, p.Category, p.DifficultyLevel,
            p.ExpectedCommitmentHours, p.Duration, p.ColorTheme, p.Status, p.ArchivedAt, p.CreatedAt,
            p.RecruitmentClosesAt, p.Members.Count(m => m.Status == "active"), p.MaxTeamCapacity,
            new ProjectLeaderSummaryDto(p.LeaderId,
                p.Leader.Profile != null ? p.Leader.Profile.DisplayName : p.Leader.Username ?? "Leader",
                p.Leader.Profile != null ? p.Leader.Profile.AvatarUrl : null, null),
            p.Positions.Where(pos => pos.Status == "open" && pos.FilledCount < pos.RequiredCount)
                .OrderBy(pos => pos.PositionId).Select(pos => new DiscoveryPosition(pos.PositionId, pos.Title, pos.RoleId,
                    pos.Role.Name, pos.ExperienceLevel, pos.RequiredCount, pos.FilledCount, pos.Status,
                    pos.PositionSkills.OrderBy(s => s.SkillId).Select(s => new DiscoverySkill(s.SkillId, s.Skill.Name, s.IsRequired)).ToList())).ToList(),
            p.DiscoveryInteractions.Any(i => i.UserId == userId && i.IsSaved)));
}
