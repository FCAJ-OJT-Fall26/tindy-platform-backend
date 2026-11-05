using Microsoft.EntityFrameworkCore;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

internal static class ProjectInvitationPendingState
{
    // Caller owns its transaction and the Project lock. Lock before User, then
    // normalize with the clock captured after any User-lock wait.
    internal static async Task LockAsync(TindyDbContext db, Guid projectId, Guid userId, CancellationToken ct) =>
        _ = await db.Database.SqlQuery<Guid>($"SELECT invitation_id AS \"Value\" FROM project_invitations WHERE project_id={projectId} AND invited_user_id={userId} AND status='pending' ORDER BY invitation_id FOR UPDATE").ToListAsync(ct);

    internal static Task<int> NormalizeAsync(TindyDbContext db, Guid projectId, Guid userId, DateTime now, CancellationToken ct) =>
        db.ProjectInvitations.Where(i => i.ProjectId == projectId && i.InvitedUserId == userId && i.Status == "pending" && i.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, "expired").SetProperty(i => i.UpdatedAt, i => i.ExpiresAt), ct);

    internal static Task<bool> HasPendingAsync(TindyDbContext db, Guid projectId, Guid userId, DateTime now, CancellationToken ct) =>
        db.ProjectInvitations.AnyAsync(i => i.ProjectId == projectId && i.InvitedUserId == userId && i.Status == "pending" && i.ExpiresAt > now, ct);
}
