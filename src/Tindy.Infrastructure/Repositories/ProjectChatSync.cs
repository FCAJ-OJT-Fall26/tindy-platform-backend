using Microsoft.EntityFrameworkCore;
using Tindy.Application.Entities;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

internal static class ProjectChatSync
{
    // Caller holds the project lock and transaction, or is creating a new project.
    public static async Task SyncAsync(TindyDbContext db, Guid projectId, DateTime now, CancellationToken ct)
    {
        var room = await db.Conversations.Include(x => x.Participants).SingleOrDefaultAsync(x => x.ProjectId == projectId, ct);
        if (room == null)
        {
            room = new Conversation { ConversationId = Guid.NewGuid(), ProjectId = projectId, Type = "project", ConsentSource = "membership", CreatedAt = now, UpdatedAt = now };
            db.Conversations.Add(room);
        }
        var members = await db.ProjectMembers.Include(x => x.User).ThenInclude(x => x.Profile).Where(x => x.ProjectId == projectId).ToListAsync(ct);
        foreach (var member in members)
        {
            var participant = room.Participants.SingleOrDefault(x => x.UserId == member.UserId);
            var active = member.Status == "active";
            if (participant == null && !active) continue;
            if (participant != null && (participant.LeftAt == null) == active) continue;
            if (participant == null)
            {
                participant = new ConversationParticipant { UserId = member.UserId, JoinedAt = member.JoinedAt };
                room.Participants.Add(participant);
            }
            participant.LeftAt = active ? null : now;
            var name = member.User.Profile?.DisplayName ?? member.User.Username ?? "Member";
            db.ChatMessages.Add(new ChatMessage { MessageId = Guid.NewGuid(), Conversation = room, SenderId = member.UserId, Body = name + (active ? " joined the project." : member.Status == "removed" ? " was removed from the project." : " left the project."), MessageType = "system", CreatedAt = now });
            room.LastMessageAt = room.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
    }
}
