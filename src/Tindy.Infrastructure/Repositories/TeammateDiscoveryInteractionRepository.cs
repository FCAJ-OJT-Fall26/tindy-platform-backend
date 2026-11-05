using Microsoft.EntityFrameworkCore;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

public sealed class TeammateDiscoveryInteractionRepository(TindyDbContext db, TimeProvider clock) : ITeammateDiscoveryInteractionRepository
{
    public async Task<TeammateInteractionsPage> GetAsync(Guid leaderId, Guid projectId, Guid positionId, string? action, int page, int pageSize, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var (project, position) = await TeammateDiscoveryEligibility.ContextAsync(db, leaderId, projectId, positionId, now, false, ct);
        var query = db.TeammateDiscoveryInteractions.AsNoTracking().Where(i => i.ProjectId == projectId && i.PositionId == positionId);
        if (action != null) query = query.Where(i => i.Action == action);
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 50);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(i => i.UpdatedAt).ThenBy(i => i.CandidateUserId)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(i => i.CandidateUserId).ToArray();
        var previews = await TeammateDiscoveryEligibility.Visible(db.Users.AsNoTracking()).Where(u => ids.Contains(u.UserId))
            .AsSplitQuery().Select(TeammateDiscoveryEligibility.Preview).ToDictionaryAsync(u => u.UserId, ct);
        var eligible = (await TeammateDiscoveryEligibility.Candidates(db, projectId, positionId, now).Where(u => ids.Contains(u.UserId)).Select(u => u.UserId).ToListAsync(ct)).ToHashSet();
        var contextOpen = project.Status == "recruiting" && project.ArchivedAt == null && project.RecruitmentClosesAt > now &&
            position.Status == "open" && position.FilledCount < position.RequiredCount &&
            await db.ProjectMembers.CountAsync(m => m.ProjectId == projectId && m.Status == "active", ct) < project.MaxTeamCapacity;
        var pages = (int)Math.Ceiling((double)total / pageSize);
        return new(rows.Select(i => new TeammateInteractionItem(i.ProjectId, i.PositionId, i.CandidateUserId, i.Action, i.CreatedAt, i.UpdatedAt,
            previews.GetValueOrDefault(i.CandidateUserId), contextOpen && eligible.Contains(i.CandidateUserId))).ToArray(), new(page, pageSize, total, pages, page < pages, page > 1));
    }

    public async Task<TeammateInteractionDto> SetAsync(Guid leaderId, Guid projectId, Guid positionId, Guid candidateId, string action, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ProjectWriteLock.AcquireAsync(db, projectId, ct);
        var (project, position) = await TeammateDiscoveryEligibility.ContextAsync(db, leaderId, projectId, positionId, clock.GetUtcNow().UtcDateTime, false, ct);
        await TeammateDiscoveryEligibility.LockPositionAsync(db, projectId, positionId, ct);
        await TeammateDiscoveryEligibility.LockUserAsync(db, candidateId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        await TeammateDiscoveryEligibility.RequireCandidateAdmissionAsync(db, project, position, candidateId, now, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO teammate_discovery_interactions (project_id,position_id,candidate_user_id,action,created_at,updated_at) VALUES ({projectId},{positionId},{candidateId},{action},{now},{now}) ON CONFLICT (project_id,position_id,candidate_user_id) DO UPDATE SET action=EXCLUDED.action, updated_at=EXCLUDED.updated_at WHERE teammate_discovery_interactions.action <> EXCLUDED.action", ct);
        var row = await db.TeammateDiscoveryInteractions.AsNoTracking().SingleAsync(i => i.ProjectId == projectId && i.PositionId == positionId && i.CandidateUserId == candidateId, ct);
        await transaction.CommitAsync(ct);
        return new(row.ProjectId, row.PositionId, row.CandidateUserId, row.Action, row.CreatedAt, row.UpdatedAt);
    }

    public async Task ResetAsync(Guid leaderId, Guid projectId, Guid positionId, Guid candidateId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ProjectWriteLock.AcquireAsync(db, projectId, ct);
        await TeammateDiscoveryEligibility.ContextAsync(db, leaderId, projectId, positionId, clock.GetUtcNow().UtcDateTime, false, ct);
        await TeammateDiscoveryEligibility.LockPositionAsync(db, projectId, positionId, ct);
        await db.TeammateDiscoveryInteractions.Where(i => i.ProjectId == projectId && i.PositionId == positionId && i.CandidateUserId == candidateId).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
