using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

public sealed class ProjectApplicationRepository(TindyDbContext db, TimeProvider clock) : IProjectApplicationRepository
{
    public async Task<ProjectApplicationDto> CreateAsync(Guid userId, Guid projectId, CreateProjectApplicationRequest request, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ProjectWriteLock.AcquireAsync(db, projectId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        if (await db.ProjectApplications.AnyAsync(a => a.ProjectId == projectId && a.ApplicantId == userId && a.Status == "pending", ct))
            throw PendingConflict();
        // Shared eligibility is evaluated again under the project lock. Rejected
        // and withdrawn history is deliberately absent from this predicate.
        if (!await DiscoveryEligibility.ForMember(db.Projects, userId, now).AnyAsync(p => p.ProjectId == projectId, ct))
            throw new ProjectException(409, "PROJECT_NOT_ELIGIBLE", "Project is not eligible for a new application.");
        if (!await db.ProjectPositions.AnyAsync(p => p.ProjectId == projectId && p.PositionId == request.PositionId &&
            p.Status == "open" && p.FilledCount < p.RequiredCount, ct))
            throw new ProjectException(409, "POSITION_UNAVAILABLE", "Selected position is not open or has no remaining slots.");
        var application = new ProjectApplication
        {
            ApplicationId = Guid.NewGuid(), ProjectId = projectId, PositionId = request.PositionId,
            ApplicantId = userId, Message = request.Message, CreatedAt = now, UpdatedAt = now
        };
        db.ProjectApplications.Add(application);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_project_applications_pending" })
        { throw PendingConflict(); }
        await transaction.CommitAsync(ct);
        return Map(application);
    }

    public async Task<ProjectApplicationDto> WithdrawAsync(Guid userId, Guid applicationId, CancellationToken ct = default)
    {
        var projectId = await db.ProjectApplications.AsNoTracking().Where(a => a.ApplicationId == applicationId && a.ApplicantId == userId)
            .Select(a => a.ProjectId).SingleOrDefaultAsync(ct);
        if (projectId == Guid.Empty) throw NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ProjectWriteLock.AcquireAsync(db, projectId, ct);
        var application = await LockedApplication(projectId, applicationId, ct);
        if (application.ApplicantId != userId) throw NotFound();
        RequirePending(application);
        application.Status = "withdrawn";
        application.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Map(application);
    }

    public async Task<ProjectApplicationDto> DecideAsync(Guid leaderId, Guid projectId, Guid applicationId, bool accept, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ProjectWriteLock.AcquireAsync(db, projectId, ct);
        var project = await db.Projects.AsNoTracking().SingleAsync(p => p.ProjectId == projectId, ct);
        if (project.LeaderId != leaderId) throw AccessDenied();
        // Lock order is project -> position -> application for every accept.
        var positionId = await db.ProjectApplications.AsNoTracking().Where(a => a.ApplicationId == applicationId && a.ProjectId == projectId)
            .Select(a => a.PositionId).SingleOrDefaultAsync(ct);
        if (positionId == Guid.Empty) throw NotFound();
        var position = await db.ProjectPositions.FromSqlInterpolated($"SELECT * FROM project_positions WHERE position_id = {positionId} AND project_id = {projectId} FOR UPDATE").SingleAsync(ct);
        await db.Entry(position).ReloadAsync(ct);
        var application = await LockedApplication(projectId, applicationId, ct);
        RequirePending(application);

        if (accept)
        {
            // Pending applications may be accepted after the recruitment cutoff.
            if (project.Status != "recruiting" || project.ArchivedAt.HasValue)
                throw new ProjectException(409, "PROJECT_NOT_RECRUITING", "Project must still be recruiting.");
            if (position.Status != "open" || position.FilledCount >= position.RequiredCount)
                throw new ProjectException(409, "POSITION_UNAVAILABLE", "Position is not open or has no remaining slots.");
            if (await db.ProjectMembers.CountAsync(m => m.ProjectId == projectId && m.Status == "active", ct) >= project.MaxTeamCapacity)
                throw new ProjectException(409, "PROJECT_CAPACITY_EXCEEDED", "Project team capacity has been reached.");
            if (!await db.Users.AnyAsync(u => u.UserId == application.ApplicantId && u.Status == "active" && u.DeletedAt == null && u.IsOnboardingCompleted, ct))
                throw new ProjectException(409, "APPLICANT_UNAVAILABLE", "Applicant account is no longer eligible.");
            var membership = await db.ProjectMembers.SingleOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == application.ApplicantId, ct);
            if (membership != null) await db.Entry(membership).ReloadAsync(ct);
            if (application.ApplicantId == project.LeaderId || membership?.Status == "active")
                throw new ProjectException(409, "ALREADY_PROJECT_MEMBER", "Applicant is already an active project member.");
            if (membership is null)
            {
                membership = new ProjectMember { ProjectId = projectId, UserId = application.ApplicantId };
                db.ProjectMembers.Add(membership);
            }
            membership.PositionId = position.PositionId;
            membership.RoleTitle = position.Title;
            membership.IsLeader = false;
            membership.Status = "active";
            membership.JoinedAt = clock.GetUtcNow().UtcDateTime;
            position.FilledCount++;
            position.UpdatedAt = membership.JoinedAt;
            if (position.FilledCount == position.RequiredCount) position.Status = "filled";
        }
        application.Status = accept ? "accepted" : "rejected";
        application.ReviewedAt = application.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        application.ReviewedBy = leaderId;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Map(application);
    }

    public async Task<MyApplicationsPage> GetMineAsync(Guid userId, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 50);
        var query = db.ProjectApplications.AsNoTracking().Where(a => a.ApplicantId == userId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.CreatedAt).ThenBy(a => a.ApplicationId)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize).Select(Summary).ToListAsync(ct);
        var pages = (int)Math.Ceiling((double)total / pageSize);
        return new(items, new(page, pageSize, total, pages, page < pages, page > 1));
    }

    public async Task<IReadOnlyList<ApplicationReviewData>> GetReviewAsync(Guid leaderId, Guid projectId, ApplicationReviewQuery query, CancellationToken ct = default)
    {
        var owner = await db.Projects.AsNoTracking().Where(p => p.ProjectId == projectId).Select(p => p.LeaderId).SingleOrDefaultAsync(ct);
        if (owner == Guid.Empty) throw new ProjectException(404, "PROJECT_NOT_FOUND", "Project not found.");
        if (owner != leaderId) throw AccessDenied();
        var applications = db.ProjectApplications.AsNoTracking().Where(a => a.ProjectId == projectId);
        if (query.Status != "all") applications = applications.Where(a => a.Status == query.Status);
        if (query.PositionId.HasValue) applications = applications.Where(a => a.PositionId == query.PositionId);
        return await applications.OrderBy(a => a.ApplicationId).AsSplitQuery().Select(a => new ApplicationReviewData(
            new ProjectApplicationDto(a.ApplicationId, a.ProjectId, a.PositionId, a.ApplicantId, a.Status, a.Message,
                a.CreatedAt, a.UpdatedAt, a.ReviewedAt, a.ReviewedBy),
            new ApplicantPreview(a.ApplicantId,
                a.Applicant.Profile != null ? a.Applicant.Profile.DisplayName : a.Applicant.Username ?? "Member",
                a.Applicant.Profile != null ? a.Applicant.Profile.PreferredRoleId : null,
                a.Applicant.Profile != null && a.Applicant.Profile.PreferredProfessionalRole != null ? a.Applicant.Profile.PreferredProfessionalRole.Name : null,
                a.Applicant.Profile != null ? a.Applicant.Profile.AvailableHoursPerWeek : null,
                a.Applicant.Profile!.UserSkills.OrderBy(s => s.SkillId).Select(s => new ApplicantSkill(s.SkillId, s.Skill.Name)).ToList(),
                a.Applicant.ProfileExperiences.Count, a.Applicant.Certificates.Count,
                "/api/users/" + a.ApplicantId + "/profile", "/api/users/" + a.ApplicantId + "/profile/experiences", "/api/users/" + a.ApplicantId + "/profile/certificates"),
            new DiscoveryPosition(a.PositionId, a.Position.Title, a.Position.RoleId, a.Position.Role.Name, a.Position.ExperienceLevel,
                a.Position.RequiredCount, a.Position.FilledCount, a.Position.Status,
                a.Position.PositionSkills.OrderBy(s => s.SkillId).Select(s => new DiscoverySkill(s.SkillId, s.Skill.Name, s.IsRequired)).ToList()),
            a.Project.ExpectedCommitmentHours)).ToListAsync(ct);
    }

    private async Task<ProjectApplication> LockedApplication(Guid projectId, Guid applicationId, CancellationToken ct)
    {
        var application = await db.ProjectApplications.FromSqlInterpolated($"SELECT * FROM project_applications WHERE project_id = {projectId} AND application_id = {applicationId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (application is null) throw NotFound();
        await db.Entry(application).ReloadAsync(ct);
        return application;
    }
    private static void RequirePending(ProjectApplication application)
    {
        if (application.Status != "pending") throw new ProjectException(409, "APPLICATION_NOT_PENDING", "Only a pending application can be changed.");
    }
    private static ProjectException NotFound() => new(404, "APPLICATION_NOT_FOUND", "Application not found.");
    private static ProjectException AccessDenied() => new(403, "PROJECT_ACCESS_DENIED", "Only the project leader can review applications.");
    private static ProjectException PendingConflict() => new(409, "APPLICATION_ALREADY_PENDING", "You already have a pending application to this project.");
    private static readonly Expression<Func<ProjectApplication, ProjectApplicationDto>> Summary = a =>
        new(a.ApplicationId, a.ProjectId, a.PositionId, a.ApplicantId, a.Status, a.Message, a.CreatedAt, a.UpdatedAt, a.ReviewedAt, a.ReviewedBy);
    private static ProjectApplicationDto Map(ProjectApplication a) => new(a.ApplicationId, a.ProjectId, a.PositionId, a.ApplicantId,
        a.Status, a.Message, a.CreatedAt, a.UpdatedAt, a.ReviewedAt, a.ReviewedBy);
}
