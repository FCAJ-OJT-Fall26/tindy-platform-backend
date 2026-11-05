using Microsoft.EntityFrameworkCore;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

public sealed class TeammateDiscoveryRepository(TindyDbContext db) : ITeammateDiscoveryRepository
{
    public async Task<TeammateDiscoveryContext> GetContextAsync(Guid leaderId, Guid projectId, Guid positionId, DateTime now, CancellationToken ct)
    {
        var (project, position) = await TeammateDiscoveryEligibility.ContextAsync(db, leaderId, projectId, positionId, now, true, ct);
        return new(projectId, TeammateDiscoveryEligibility.Position(position), project.ExpectedCommitmentHours);
    }

    public async Task<IReadOnlyList<TeammateCandidateData>> GetCandidatesAsync(Guid projectId, Guid positionId, DateTime now, TeammateDiscoveryFilters filters, CancellationToken ct)
    {
        var query = TeammateDiscoveryEligibility.Candidates(db, projectId, positionId, now);
        if (filters.RoleId.HasValue) query = query.Where(u => u.Profile!.PreferredRoleId == filters.RoleId);
        if (filters.SkillId.HasValue) query = query.Where(u => u.Profile!.UserSkills.Any(s => s.SkillId == filters.SkillId));
        if (filters.MinAvailableHoursPerWeek.HasValue) query = query.Where(u => u.Profile!.AvailableHoursPerWeek >= filters.MinAvailableHoursPerWeek);
        var candidates = await query.OrderBy(u => u.UserId).AsSplitQuery().Select(TeammateDiscoveryEligibility.Preview).ToListAsync(ct);
        var actions = await db.TeammateDiscoveryInteractions.AsNoTracking().Where(i => i.ProjectId == projectId && i.PositionId == positionId)
            .ToDictionaryAsync(i => i.CandidateUserId, i => i.Action, ct);
        return candidates.Select(c => new TeammateCandidateData(c, actions.GetValueOrDefault(c.UserId))).ToArray();
    }
}
