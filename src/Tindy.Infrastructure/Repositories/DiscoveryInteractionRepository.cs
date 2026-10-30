using Microsoft.EntityFrameworkCore;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

public sealed class DiscoveryInteractionRepository(TindyDbContext db) : IDiscoveryInteractionRepository
{
    public async Task SetAsync(Guid userId, Guid projectId, bool savedFlag, bool value, DateTime now, CancellationToken ct = default)
    {
        if (!value)
        {
            // DELETE is idempotent even if the row/project no longer exists.
            if (savedFlag)
                await db.DiscoveryProjectInteractions.Where(i => i.UserId == userId && i.ProjectId == projectId)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.IsSaved, false).SetProperty(i => i.UpdatedAt, now), ct);
            else
                await db.DiscoveryProjectInteractions.Where(i => i.UserId == userId && i.ProjectId == projectId)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.IsSkipped, false).SetProperty(i => i.UpdatedAt, now), ct);
            return;
        }
        if (!await db.Projects.AnyAsync(p => p.ProjectId == projectId, ct))
            throw new ProjectException(404, "PROJECT_NOT_FOUND", "Project not found.");
        // Each UPSERT changes only its own flag, so concurrent save/skip cannot
        // erase one another. There is no interested/applied shadow state.
        if (savedFlag)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO discovery_project_interactions (user_id, project_id, is_saved, is_skipped, updated_at) VALUES ({userId}, {projectId}, TRUE, FALSE, {now}) ON CONFLICT (user_id, project_id) DO UPDATE SET is_saved = TRUE, updated_at = EXCLUDED.updated_at", ct);
        else
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO discovery_project_interactions (user_id, project_id, is_saved, is_skipped, updated_at) VALUES ({userId}, {projectId}, FALSE, TRUE, {now}) ON CONFLICT (user_id, project_id) DO UPDATE SET is_skipped = TRUE, updated_at = EXCLUDED.updated_at", ct);
    }

    public async Task<SavedDiscoveryPage> GetSavedAsync(Guid userId, int page, int pageSize, DateTime now, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 50);
        var query = db.DiscoveryProjectInteractions.AsNoTracking().Where(i => i.UserId == userId && i.IsSaved);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(i => i.UpdatedAt).ThenBy(i => i.ProjectId)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize).Select(i => new SavedDiscoveryProject(i.ProjectId, i.Project.Name,
                i.Project.Tagline, i.Project.Category, i.Project.Status, i.Project.RecruitmentClosesAt,
                i.Project.Status == "recruiting" && i.Project.ArchivedAt == null && i.Project.RecruitmentClosesAt > now,
                i.IsSkipped, i.UpdatedAt)).ToListAsync(ct);
        var totalPages = (int)Math.Ceiling((double)total / pageSize);
        return new(items, new(page, pageSize, total, totalPages, page < totalPages, page > 1));
    }
}
