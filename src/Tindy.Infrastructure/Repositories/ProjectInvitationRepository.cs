using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

public sealed class ProjectInvitationRepository(TindyDbContext db, TimeProvider clock) : IProjectInvitationRepository
{
    public async Task<ProjectInvitationWriteResult> SendAsync(Guid leaderId, Guid projectId, CreateProjectInvitationRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ProjectWriteLock.AcquireAsync(db, projectId, ct);
        var (project, position) = await TeammateDiscoveryEligibility.ContextAsync(db, leaderId, projectId, request.PositionId, clock.GetUtcNow().UtcDateTime, false, ct);
        await TeammateDiscoveryEligibility.LockPositionAsync(db, projectId, request.PositionId, ct);
        // Do not mutate history for a private/unavailable target that was never visible.
        if (request.InvitedUserId != leaderId && !await TeammateDiscoveryEligibility.Visible(db.Users).AnyAsync(u => u.UserId == request.InvitedUserId, ct))
            throw new ProjectException(404, "CANDIDATE_NOT_FOUND", "Candidate not found.");
        await ProjectInvitationPendingState.LockAsync(db, projectId, request.InvitedUserId, ct);
        await TeammateDiscoveryEligibility.LockUserAsync(db, request.InvitedUserId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        await ProjectInvitationPendingState.NormalizeAsync(db, projectId, request.InvitedUserId, now, ct);
        try { await TeammateDiscoveryEligibility.RequireCandidateAdmissionAsync(db, project, position, request.InvitedUserId, now, ct); }
        catch (ProjectException error)
        {
            await transaction.CommitAsync(ct);
            return new(null, error);
        }
        var invitation = new ProjectInvitation
        {
            InvitationId = Guid.NewGuid(), ProjectId = projectId, PositionId = request.PositionId,
            InvitedUserId = request.InvitedUserId, InvitingLeaderId = leaderId, Message = request.Message,
            CreatedAt = now, UpdatedAt = now, ExpiresAt = now.AddHours(168)
        };
        db.ProjectInvitations.Add(invitation);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_project_invitations_pending" })
        {
            db.Entry(invitation).State = EntityState.Detached;
            await transaction.CommitAsync(ct);
            return new(null, new(409, "INVITATION_ALREADY_PENDING", "A pending invitation already exists for this member and project."));
        }
        await AddNotificationAsync(invitation, "invitation_received", invitation.InvitedUserId, leaderId, project.Name, position.Title, now, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(Map(invitation, now));
    }

    public async Task<ProjectInvitationWriteResult> ChangeAsync(Guid userId, Guid? routeProjectId, Guid invitationId, string action, CancellationToken ct)
    {
        // Recipient lookup is deliberately scoped before resolving the Project lock.
        var lookup = routeProjectId.HasValue ? null : await db.ProjectInvitations.AsNoTracking()
            .Where(i => i.InvitationId == invitationId && i.InvitedUserId == userId).Select(i => new { i.ProjectId, i.PositionId }).SingleOrDefaultAsync(ct);
        if (!routeProjectId.HasValue && lookup == null) throw NotFound();
        var projectId = routeProjectId ?? lookup!.ProjectId;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ProjectWriteLock.AcquireAsync(db, projectId, ct);
        var project = await db.Projects.AsNoTracking().SingleAsync(p => p.ProjectId == projectId, ct);
        if (routeProjectId.HasValue && project.LeaderId != userId) throw AccessDenied();
        var positionId = lookup?.PositionId ?? await db.ProjectInvitations.AsNoTracking()
            .Where(i => i.InvitationId == invitationId && i.ProjectId == projectId).Select(i => i.PositionId).SingleOrDefaultAsync(ct);
        if (positionId == Guid.Empty) throw NotFound();
        await TeammateDiscoveryEligibility.LockPositionAsync(db, projectId, positionId, ct);
        var invitation = await db.ProjectInvitations.FromSqlInterpolated($"SELECT * FROM project_invitations WHERE project_id={projectId} AND invitation_id={invitationId} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw NotFound();
        await db.Entry(invitation).ReloadAsync(ct);
        if ((!routeProjectId.HasValue && invitation.InvitedUserId != userId) || invitation.PositionId != positionId) throw NotFound();
        if (invitation.Status != "pending") return new(null, StateConflict(invitation.Status));
        if (action == "accept") await TeammateDiscoveryEligibility.LockUserAsync(db, invitation.InvitedUserId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        if (invitation.ExpiresAt <= now)
        {
            invitation.Status = "expired"; invitation.UpdatedAt = invitation.ExpiresAt;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(null, StateConflict("expired"));
        }
        if (action == "accept")
        {
            if (project.Status != "recruiting" || project.ArchivedAt.HasValue)
                throw new ProjectException(409, "PROJECT_NOT_RECRUITING", "Project must still be recruiting.");
            if (invitation.InvitingLeaderId != project.LeaderId)
                throw new ProjectException(409, "INVITATION_SENDER_UNAVAILABLE", "The original sender is no longer the actual project leader.");
            var position = await db.ProjectPositions.SingleAsync(p => p.PositionId == positionId && p.ProjectId == projectId, ct);
            await db.Entry(position).ReloadAsync(ct);
            if (position.Status != "open" || position.FilledCount >= position.RequiredCount)
                throw new ProjectException(409, "POSITION_UNAVAILABLE", "Position is not open or has no remaining slots.");
            if (!await TeammateDiscoveryEligibility.Visible(db.Users).AnyAsync(u => u.UserId == invitation.InvitedUserId, ct))
                throw new ProjectException(409, "CANDIDATE_UNAVAILABLE", "Your account or invitation opt-in is no longer eligible.");
            if (await db.ProjectApplications.AnyAsync(a => a.ProjectId == projectId && a.ApplicantId == invitation.InvitedUserId && a.Status == "pending", ct))
                throw new ProjectException(409, "APPLICATION_ALREADY_PENDING", "A pending application already exists for this member and project.");
            if (await db.ProjectMembers.CountAsync(m => m.ProjectId == projectId && m.Status == "active", ct) >= project.MaxTeamCapacity)
                throw new ProjectException(409, "PROJECT_CAPACITY_EXCEEDED", "Project team capacity has been reached.");
            var member = await db.ProjectMembers.SingleOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == invitation.InvitedUserId, ct);
            if (member != null) await db.Entry(member).ReloadAsync(ct);
            if (invitation.InvitedUserId == project.LeaderId || member?.Status == "active")
                throw new ProjectException(409, "ALREADY_PROJECT_MEMBER", "You are already an active project member.");
            if (member == null)
            {
                member = new ProjectMember { ProjectId = projectId, UserId = invitation.InvitedUserId };
                db.ProjectMembers.Add(member);
            }
            member.PositionId = positionId; member.RoleTitle = position.Title; member.IsLeader = false; member.Status = "active"; member.JoinedAt = now;
            position.FilledCount++; position.UpdatedAt = now;
            if (position.FilledCount == position.RequiredCount) position.Status = "filled";
        }
        invitation.Status = action switch { "accept" => "accepted", "decline" => "declined", _ => "cancelled" };
        invitation.UpdatedAt = now;
        if (action != "cancel") invitation.RespondedAt = now;
        // Project is protected by the existing project write lock. In particular,
        // decline must notify the current authorized leader, never the old sender.
        var recipientId = action == "cancel" ? invitation.InvitedUserId : project.LeaderId;
        var positionTitle = await db.ProjectPositions.Where(p => p.PositionId == positionId && p.ProjectId == projectId)
            .Select(p => p.Title).SingleAsync(ct);
        await AddNotificationAsync(invitation, "invitation_" + invitation.Status, recipientId, userId, project.Name, positionTitle, now, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(Map(invitation, now));
    }

    public async Task<ProjectInvitationsPage> GetForLeaderAsync(Guid leaderId, Guid projectId, ProjectInvitationQuery query, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.ProjectId == projectId, ct)
            ?? throw new ProjectException(404, "PROJECT_NOT_FOUND", "Project not found.");
        if (project.LeaderId != leaderId) throw AccessDenied();
        if (query.PositionId.HasValue && !await db.ProjectPositions.AnyAsync(p => p.ProjectId == projectId && p.PositionId == query.PositionId, ct))
            throw new ProjectException(404, "POSITION_NOT_FOUND", "Position not found in this project.");
        var rows = db.ProjectInvitations.AsNoTracking().Where(i => i.ProjectId == projectId);
        if (query.PositionId.HasValue) rows = rows.Where(i => i.PositionId == query.PositionId);
        if (query.InvitedUserId.HasValue) rows = rows.Where(i => i.InvitedUserId == query.InvitedUserId);
        return await ListAsync(rows, query, ct);
    }

    public Task<ProjectInvitationsPage> GetMineAsync(Guid userId, ProjectInvitationQuery query, CancellationToken ct)
    {
        var rows = db.ProjectInvitations.AsNoTracking().Where(i => i.InvitedUserId == userId);
        if (query.ProjectId.HasValue) rows = rows.Where(i => i.ProjectId == query.ProjectId);
        return ListAsync(rows, query, ct);
    }

    private async Task<ProjectInvitationsPage> ListAsync(IQueryable<ProjectInvitation> rows, ProjectInvitationQuery query, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (query.Status == "pending") rows = rows.Where(i => i.Status == "pending" && i.ExpiresAt > now);
        else if (query.Status == "expired") rows = rows.Where(i => i.Status == "expired" || (i.Status == "pending" && i.ExpiresAt <= now));
        else if (query.Status != "all") rows = rows.Where(i => i.Status == query.Status);
        var page = Math.Max(1, query.Page); var size = Math.Clamp(query.PageSize, 1, 50);
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(i => i.CreatedAt).ThenBy(i => i.InvitationId)
            .Skip((int)Math.Min((long)(page - 1) * size, int.MaxValue)).Take(size)
            .Select(i => new ProjectInvitationDto(i.InvitationId, i.ProjectId, i.PositionId, i.InvitedUserId, i.InvitingLeaderId, i.Message,
                i.Status == "pending" && i.ExpiresAt <= now ? "expired" : i.Status, i.CreatedAt,
                i.Status == "pending" && i.ExpiresAt <= now ? i.ExpiresAt : i.UpdatedAt, i.ExpiresAt, i.RespondedAt)).ToListAsync(ct);
        var pages = (int)Math.Ceiling((double)total / size);
        return new(items, new(page, size, total, pages, page < pages, page > 1));
    }

    private async Task AddNotificationAsync(ProjectInvitation invitation, string type, Guid recipientId, Guid actorId,
        string projectName, string positionTitle, DateTime now, CancellationToken ct)
    {
        var actorName = await db.Users.Where(u => u.UserId == actorId)
            .Select(u => u.Profile != null ? u.Profile.DisplayName ?? u.Username : u.Username).SingleAsync(ct);
        db.Notifications.Add(new Notification
        {
            NotificationId = Guid.NewGuid(), RecipientUserId = recipientId, ActorUserId = actorId,
            InvitationId = invitation.InvitationId, Type = type, CreatedAt = now,
            Payload = JsonSerializer.SerializeToElement(new
            {
                project_name = projectName, position_title = positionTitle, actor_display_name = actorName ?? "Member"
            })
        });
    }

    private static ProjectInvitationDto Map(ProjectInvitation i, DateTime now) => new(i.InvitationId, i.ProjectId, i.PositionId, i.InvitedUserId,
        i.InvitingLeaderId, i.Message, i.Status == "pending" && i.ExpiresAt <= now ? "expired" : i.Status, i.CreatedAt,
        i.Status == "pending" && i.ExpiresAt <= now ? i.ExpiresAt : i.UpdatedAt, i.ExpiresAt, i.RespondedAt);
    private static ProjectException NotFound() => new(404, "INVITATION_NOT_FOUND", "Invitation not found.");
    private static ProjectException AccessDenied() => new(403, "PROJECT_ACCESS_DENIED", "Only the actual project leader can manage invitations.");
    private static ProjectException StateConflict(string status) => status == "expired"
        ? new(409, "INVITATION_EXPIRED", "Invitation has expired.") : new(409, "INVITATION_NOT_PENDING", "Only a pending invitation can be changed.");
}
