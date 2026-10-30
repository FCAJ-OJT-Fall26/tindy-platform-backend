using Microsoft.EntityFrameworkCore;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Entities;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Persistence;
using Tindy.Application.Exceptions;
using Tindy.Application.Services;

namespace Tindy.Infrastructure.Repositories;

public sealed class ProjectRepository : IProjectRepository
{
    private readonly TindyDbContext _context;
    private readonly TimeProvider _clock;

    public ProjectRepository(TindyDbContext context, TimeProvider? clock = null)
    {
        _context = context;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<Project?> GetByIdAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return await _context.Projects
            .Include(p => p.Members)
            .SingleOrDefaultAsync(p => p.ProjectId == projectId, cancellationToken);
    }

    public async Task<Project?> GetByIdWithDetailsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return await _context.Projects
            .Include(p => p.Leader)
                .ThenInclude(u => u.Profile)
            .Include(p => p.Members)
                .ThenInclude(m => m.User)
                    .ThenInclude(u => u.Profile)
            .Include(p => p.Positions)
                .ThenInclude(pos => pos.Role)
            .Include(p => p.Positions)
                .ThenInclude(pos => pos.PositionSkills)
                    .ThenInclude(ps => ps.Skill)
            .SingleOrDefaultAsync(p => p.ProjectId == projectId, cancellationToken);
    }

    public async Task<Project> CreateAsync(Project project, CancellationToken cancellationToken = default)
    {
        _context.Projects.Add(project);
        await _context.SaveChangesAsync(cancellationToken);
        return project;
    }

    public async Task UpdateAsync(Project project, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await ProjectWriteLock.AcquireAsync(_context, project.ProjectId, cancellationToken);
        var stored = await _context.Entry(project).GetDatabaseValuesAsync(cancellationToken);
        if (stored is null || stored.GetValue<string>(nameof(Project.Status)) == "archived")
            throw new ProjectException(404, "PROJECT_NOT_FOUND", "Project not found.");
        var active = await _context.ProjectMembers.CountAsync(m => m.ProjectId == project.ProjectId && m.Status == "active", cancellationToken);
        if (project.MaxTeamCapacity < active || project.MaxTeamCapacity < Math.Max(1, stored.GetValue<int>(nameof(Project.InitialTeamSize))))
            throw new ProjectException(400, "PROJECT_CAPACITY_EXCEEDED", "Max team capacity cannot be less than the current team size.");
        _context.ChangeTracker.DetectChanges();
        if (_context.Entry(project).Property(p => p.RecruitmentClosesAt).IsModified)
            RecruitmentDeadline.CreateCutoff(RecruitmentDeadline.ClosingDate(project.RecruitmentClosesAt), _clock.GetUtcNow().UtcDateTime);
        project.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<Project> Items, int TotalCount)> GetPagedProjectsAsync(
        GetProjectsQuery query,
        CancellationToken cancellationToken = default)
    {
        var q = _context.Projects
            .Include(p => p.Leader)
                .ThenInclude(u => u.Profile)
            .Include(p => p.Members)
            .Include(p => p.Positions)
                .ThenInclude(pos => pos.Role)
            .Include(p => p.Positions)
                .ThenInclude(pos => pos.PositionSkills)
                    .ThenInclude(ps => ps.Skill)
            .AsNoTracking();

        // Status filter: default recruiting, or specified, or all
        if (string.IsNullOrWhiteSpace(query.Status) || query.Status.Equals("recruiting", StringComparison.OrdinalIgnoreCase))
        {
            q = q.Where(p => p.Status == "recruiting");
        }
        else if (!query.Status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var st = query.Status.Trim().ToLower();
            q = q.Where(p => p.Status.ToLower() == st);
        }

        // Search q: name or description
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var search = query.Q.Trim().ToLower();
            q = q.Where(p => p.Name.ToLower().Contains(search) || p.Description.ToLower().Contains(search));
        }

        // Category filter
        if (!string.IsNullOrWhiteSpace(query.Category) && !query.Category.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var cat = query.Category.Trim().ToLower();
            q = q.Where(p => p.Category.ToLower() == cat);
        }

        // Role filter
        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            if (Guid.TryParse(query.Role, out var roleId))
            {
                q = q.Where(p => p.Positions.Any(pos => pos.RoleId == roleId));
            }
            else
            {
                var roleName = query.Role.Trim().ToLower();
                q = q.Where(p => p.Positions.Any(pos => pos.Title.ToLower().Contains(roleName) || pos.Role.Name.ToLower().Contains(roleName)));
            }
        }

        // Skill filter
        if (!string.IsNullOrWhiteSpace(query.Skill))
        {
            if (Guid.TryParse(query.Skill, out var skillId))
            {
                q = q.Where(p => p.Positions.Any(pos => pos.PositionSkills.Any(ps => ps.SkillId == skillId)));
            }
            else
            {
                var skillName = query.Skill.Trim().ToLower();
                q = q.Where(p => p.Positions.Any(pos => pos.PositionSkills.Any(ps => ps.Skill.Name.ToLower().Contains(skillName))));
            }
        }

        // SortBy: newest, deadline, popular
        q = (query.SortBy?.ToLowerInvariant()) switch
        {
            "deadline" => q.OrderBy(p => p.RecruitmentClosesAt).ThenBy(p => p.ProjectId),
            "popular" => q.OrderByDescending(p => p.Members.Count),
            _ => q.OrderByDescending(p => p.CreatedAt)
        };

        var totalCount = await q.CountAsync(cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 50);

        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, totalCount);
    }

    public async Task<IReadOnlyList<Project>> GetLeadingProjectsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.Projects
            .Include(p => p.Members)
            .Include(p => p.Applications.Where(a => a.Status == "pending"))
            .AsSplitQuery()
            .Where(p => p.LeaderId == userId && p.Status != "archived")
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectMember>> GetJoinedMembershipsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.ProjectMembers
            .Include(m => m.Project)
                .ThenInclude(p => p.Leader)
                    .ThenInclude(u => u.Profile)
            .Where(m => m.UserId == userId && !m.IsLeader && m.Status == "active" && m.Project.Status != "archived")
            .OrderByDescending(m => m.JoinedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> RoleExistsAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        return await _context.ProfessionalRoles.AnyAsync(r => r.ProfessionalRoleId == roleId && r.IsActive, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ValidateSkillIdsAsync(IEnumerable<Guid> skillIds, CancellationToken cancellationToken = default)
    {
        var list = skillIds.Distinct().ToList();
        return await _context.Skills
            .Where(s => list.Contains(s.SkillId) && s.IsActive)
            .Select(s => s.SkillId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectPosition>> GetPositionsByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return await _context.ProjectPositions
            .Include(p => p.Role)
            .Include(p => p.PositionSkills)
                .ThenInclude(ps => ps.Skill)
            .Where(p => p.ProjectId == projectId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ProjectPosition?> GetPositionWithDetailsAsync(Guid projectId, Guid positionId, CancellationToken cancellationToken = default)
    {
        return await _context.ProjectPositions
            .Include(p => p.Role)
            .Include(p => p.Project)
            .Include(p => p.PositionSkills)
                .ThenInclude(ps => ps.Skill)
            .SingleOrDefaultAsync(p => p.ProjectId == projectId && p.PositionId == positionId, cancellationToken);
    }

    public async Task<ProjectPosition> AddPositionAsync(ProjectPosition position, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await LockEditableProjectAsync(position.ProjectId, cancellationToken);
        _context.ProjectPositions.Add(position);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return position;
    }

    public async Task UpdatePositionAsync(ProjectPosition position, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await LockEditableProjectAsync(position.ProjectId, cancellationToken);
        var stored = await _context.Entry(position).GetDatabaseValuesAsync(cancellationToken);
        if (stored is null) throw new ProjectException(404, "POSITION_NOT_FOUND", "Position not found.");
        _context.ChangeTracker.DetectChanges();
        var entry = _context.Entry(position);
        if (!entry.Property(p => p.RequiredCount).IsModified) position.RequiredCount = stored.GetValue<int>(nameof(ProjectPosition.RequiredCount));
        if (!entry.Property(p => p.Status).IsModified) position.Status = stored.GetValue<string>(nameof(ProjectPosition.Status));
        position.FilledCount = stored.GetValue<int>(nameof(ProjectPosition.FilledCount));
        if (position.RequiredCount < position.FilledCount)
            throw new ProjectException(409, "POSITION_CAPACITY_EXCEEDED", "Required count cannot be below filled count.");
        if (position.Status == "open" && position.FilledCount == position.RequiredCount) position.Status = "filled";
        position.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeletePositionAsync(ProjectPosition position, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await LockEditableProjectAsync(position.ProjectId, cancellationToken);
        if (await _context.ProjectMembers.AnyAsync(m => m.PositionId == position.PositionId, cancellationToken) ||
            await _context.ProjectApplications.AnyAsync(a => a.PositionId == position.PositionId, cancellationToken) ||
            await _context.ProjectPositions.AnyAsync(p => p.PositionId == position.PositionId && p.FilledCount > 0, cancellationToken))
            throw new ProjectException(409, "POSITION_IN_USE", "A position with members or application history cannot be deleted. Close it instead.");
        _context.ProjectPositions.Remove(position);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task LockEditableProjectAsync(Guid projectId, CancellationToken ct)
    {
        await ProjectWriteLock.AcquireAsync(_context, projectId, ct);
        if (!await _context.Projects.AnyAsync(p => p.ProjectId == projectId && p.Status != "archived" && p.ArchivedAt == null, ct))
            throw new ProjectException(404, "PROJECT_NOT_FOUND", "Project not found.");
    }

    public async Task<Skill?> GetSkillByIdAsync(Guid skillId, CancellationToken cancellationToken = default)
    {
        return await _context.Skills
            .SingleOrDefaultAsync(s => s.SkillId == skillId && s.IsActive, cancellationToken);
    }

    public async Task<ProjectPositionSkill?> GetPositionSkillAsync(Guid positionId, Guid skillId, CancellationToken cancellationToken = default)
    {
        return await _context.PositionSkills
            .Include(ps => ps.Skill)
            .SingleOrDefaultAsync(ps => ps.PositionId == positionId && ps.SkillId == skillId, cancellationToken);
    }

    public async Task AddPositionSkillAsync(ProjectPositionSkill positionSkill, CancellationToken cancellationToken = default)
    {
        _context.PositionSkills.Add(positionSkill);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeletePositionSkillAsync(ProjectPositionSkill positionSkill, CancellationToken cancellationToken = default)
    {
        _context.PositionSkills.Remove(positionSkill);
        await _context.SaveChangesAsync(cancellationToken);
    }
}

