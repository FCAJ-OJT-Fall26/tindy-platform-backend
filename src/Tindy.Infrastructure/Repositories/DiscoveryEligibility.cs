using Tindy.Application.DTOs.Projects;
using Tindy.Application.Entities;

namespace Tindy.Infrastructure.Repositories;

// The same relational eligibility query is reused when creating applications.
public static class DiscoveryEligibility
{
    public static IQueryable<Project> ForMember(IQueryable<Project> projects, Guid userId, DateTime now) => projects.Where(p =>
        p.Status == "recruiting" && p.ArchivedAt == null && p.RecruitmentClosesAt > now &&
        p.LeaderId != userId && p.Members.Count(m => m.Status == "active") < p.MaxTeamCapacity &&
        !p.Members.Any(m => m.UserId == userId && m.Status == "active") &&
        !p.Applications.Any(a => a.ApplicantId == userId && a.Status == "pending") &&
        p.Positions.Any(pos => pos.Status == "open" && pos.FilledCount < pos.RequiredCount));

    public static IQueryable<Project> Filter(IQueryable<Project> projects, DiscoveryFilters filters)
    {
        if (!string.IsNullOrWhiteSpace(filters.Category))
            projects = projects.Where(p => p.Category == filters.Category.Trim());
        if (filters.RoleId.HasValue || filters.SkillId.HasValue)
            projects = projects.Where(p => p.Positions.Any(pos => pos.Status == "open" && pos.FilledCount < pos.RequiredCount &&
                (!filters.RoleId.HasValue || pos.RoleId == filters.RoleId) &&
                (!filters.SkillId.HasValue || pos.PositionSkills.Any(s => s.SkillId == filters.SkillId))));
        return projects;
    }
}
