using Microsoft.EntityFrameworkCore;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Entities;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

public sealed class ProjectRepository : IProjectRepository
{
    private readonly TindyDbContext _context;

    public ProjectRepository(TindyDbContext context)
    {
        _context = context;
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
        project.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
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
            "deadline" => q.OrderBy(p => p.CreatedAt.AddDays(p.RecruitmentClosingDays)),
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
}
