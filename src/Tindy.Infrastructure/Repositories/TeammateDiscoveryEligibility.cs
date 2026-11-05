using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

internal static class TeammateDiscoveryEligibility
{
    internal static IQueryable<User> Visible(IQueryable<User> users) => users.Where(u => u.Status == "active" &&
        u.DeletedAt == null && u.IsOnboardingCompleted && u.Profile != null && u.Profile.OpenToTeamInvitations);

    internal static IQueryable<User> Candidates(TindyDbContext db, Guid projectId, Guid positionId, DateTime now, bool includeSkipped = false) =>
        Visible(db.Users.AsNoTracking()).Where(u =>
            !db.Projects.Any(p => p.ProjectId == projectId && p.LeaderId == u.UserId) &&
            !db.ProjectMembers.Any(m => m.ProjectId == projectId && m.UserId == u.UserId && m.Status == "active") &&
            !db.ProjectApplications.Any(a => a.ProjectId == projectId && a.ApplicantId == u.UserId && a.Status == "pending") &&
            !db.ProjectInvitations.Any(i => i.ProjectId == projectId && i.InvitedUserId == u.UserId &&
                ((i.Status == "pending" && i.ExpiresAt > now) || (i.Status == "declined" && i.RespondedAt > now.AddHours(-168)))) &&
            (includeSkipped || !db.TeammateDiscoveryInteractions.Any(i => i.ProjectId == projectId && i.PositionId == positionId && i.CandidateUserId == u.UserId && i.Action == "skipped")));

    internal static readonly Expression<Func<User, TeammateCandidatePreview>> Preview = u => new(u.UserId,
        u.Profile!.DisplayName, u.Profile.Headline, u.Profile.AvatarUrl, u.Profile.PreferredRoleId,
        u.Profile.PreferredProfessionalRole == null ? null : u.Profile.PreferredProfessionalRole.Name,
        u.Profile.AvailableHoursPerWeek, u.Profile.UserSkills.OrderBy(s => s.SkillId).Select(s => new ApplicantSkill(s.SkillId, s.Skill.Name)).ToList(),
        u.ProfileExperiences.Count, u.Certificates.Count, "/api/users/" + u.UserId + "/profile",
        "/api/users/" + u.UserId + "/profile/experiences", "/api/users/" + u.UserId + "/profile/certificates");

    internal static async Task<(Project Project, ProjectPosition Position)> ContextAsync(TindyDbContext db, Guid leaderId,
        Guid projectId, Guid positionId, DateTime now, bool requireAdmission, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.ProjectId == projectId, ct)
            ?? throw new ProjectException(404, "PROJECT_NOT_FOUND", "Project not found.");
        if (project.LeaderId != leaderId) throw new ProjectException(403, "PROJECT_ACCESS_DENIED", "Only the actual project leader can manage candidates.");
        var position = await db.ProjectPositions.AsNoTracking().Include(p => p.Role).Include(p => p.PositionSkills).ThenInclude(s => s.Skill)
            .AsSplitQuery().SingleOrDefaultAsync(p => p.ProjectId == projectId && p.PositionId == positionId, ct)
            ?? throw new ProjectException(404, "POSITION_NOT_FOUND", "Position not found in this project.");
        if (requireAdmission) await RequireAdmissionAsync(db, project, position, now, ct);
        return (project, position);
    }

    internal static async Task RequireAdmissionAsync(TindyDbContext db, Project project, ProjectPosition position, DateTime now, CancellationToken ct)
    {
        if (project.Status != "recruiting" || project.ArchivedAt != null || project.RecruitmentClosesAt <= now)
            throw new ProjectException(409, "PROJECT_NOT_ELIGIBLE", "Project is not eligible for new invitations.");
        if (await db.ProjectMembers.CountAsync(m => m.ProjectId == project.ProjectId && m.Status == "active", ct) >= project.MaxTeamCapacity)
            throw new ProjectException(409, "PROJECT_CAPACITY_EXCEEDED", "Project team capacity has been reached.");
        if (position.Status != "open" || position.FilledCount >= position.RequiredCount)
            throw new ProjectException(409, "POSITION_UNAVAILABLE", "Position is not open or has no remaining slots.");
    }

    internal static DiscoveryPosition Position(ProjectPosition p) => new(p.PositionId, p.Title, p.RoleId, p.Role.Name,
        p.ExperienceLevel, p.RequiredCount, p.FilledCount, p.Status,
        p.PositionSkills.OrderBy(s => s.SkillId).Select(s => new DiscoverySkill(s.SkillId, s.Skill.Name, s.IsRequired)).ToArray());

    // Caller owns Project -> Position -> candidate User locks. Skip is intentionally
    // absent: explicit interaction changes and invitations can restore a skipped user.
    internal static async Task RequireCandidateAdmissionAsync(TindyDbContext db, Project project, ProjectPosition position, Guid candidateId, DateTime now, CancellationToken ct)
    {
        if (candidateId != project.LeaderId && !await Visible(db.Users).AnyAsync(u => u.UserId == candidateId, ct))
            throw new ProjectException(404, "CANDIDATE_NOT_FOUND", "Candidate not found.");
        if (await db.ProjectInvitations.AnyAsync(i => i.ProjectId == project.ProjectId && i.InvitedUserId == candidateId && i.Status == "pending" && i.ExpiresAt > now, ct))
            throw new ProjectException(409, "INVITATION_ALREADY_PENDING", "A pending invitation already exists for this member and project.");
        if (await db.ProjectApplications.AnyAsync(a => a.ProjectId == project.ProjectId && a.ApplicantId == candidateId && a.Status == "pending", ct))
            throw new ProjectException(409, "APPLICATION_ALREADY_PENDING", "A pending application already exists for this member and project.");
        if (await db.ProjectInvitations.AnyAsync(i => i.ProjectId == project.ProjectId && i.InvitedUserId == candidateId && i.Status == "declined" && i.RespondedAt > now.AddHours(-168), ct))
            throw new ProjectException(409, "INVITATION_COOLDOWN_ACTIVE", "This project must wait 168 hours after the member declined before inviting again.");
        if (candidateId == project.LeaderId) throw new ProjectException(409, "CANNOT_INVITE_SELF", "You cannot invite yourself.");
        if (await db.ProjectMembers.AnyAsync(m => m.ProjectId == project.ProjectId && m.UserId == candidateId && m.Status == "active", ct))
            throw new ProjectException(409, "ALREADY_PROJECT_MEMBER", "Candidate is already an active project member.");
        await RequireAdmissionAsync(db, project, position, now, ct);
    }

    internal static async Task LockPositionAsync(TindyDbContext db, Guid projectId, Guid positionId, CancellationToken ct)
    {
        var id = await db.Database.SqlQuery<Guid>($"SELECT position_id AS \"Value\" FROM project_positions WHERE project_id={projectId} AND position_id={positionId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (id == Guid.Empty) throw new ProjectException(404, "POSITION_NOT_FOUND", "Position not found in this project.");
    }

    internal static Task<Guid> LockUserAsync(TindyDbContext db, Guid userId, CancellationToken ct) =>
        // NO KEY UPDATE conflicts with Profile/account writes, while remaining
        // compatible with FK KEY SHARE locks (e.g. reciprocal invitations).
        db.Database.SqlQuery<Guid>($"SELECT user_id AS \"Value\" FROM users WHERE user_id={userId} FOR NO KEY UPDATE").SingleOrDefaultAsync(ct);
}
