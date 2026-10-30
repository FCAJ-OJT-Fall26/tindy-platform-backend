using Microsoft.EntityFrameworkCore;
using Tindy.Application.Exceptions;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

internal static class ProjectWriteLock
{
    // Call only inside the caller's transaction. All capacity/position writes
    // acquire this lock first, then position/application locks in that order.
    public static async Task AcquireAsync(TindyDbContext db, Guid projectId, CancellationToken ct)
    {
        var id = await db.Database.SqlQuery<Guid>($"SELECT project_id AS \"Value\" FROM projects WHERE project_id = {projectId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (id == Guid.Empty) throw new ProjectException(404, "PROJECT_NOT_FOUND", "Project not found.");
    }
}
