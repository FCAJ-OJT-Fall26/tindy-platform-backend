using Microsoft.EntityFrameworkCore;
using Tindy.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Tindy.Application.DTOs.Chat;
using Tindy.Application.Interfaces.Services;
namespace Tindy.Api.Hubs;

public sealed class ChatNotifier(IHubContext<ChatHub> hub, ILogger<ChatNotifier> logger, TindyDbContext db) : IChatNotifier
{
    public async Task MessageCreatedAsync(IEnumerable<Guid> users, ChatMessageDto message, CancellationToken ct)
    {
        var projectId = await db.Conversations.Where(c => c.ConversationId == message.ConversationId).Select(c => c.ProjectId).SingleOrDefaultAsync(ct);
        if (projectId == null) { await Publish(users, "message.created", message); return; }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT project_id FROM projects WHERE project_id = {projectId.Value} FOR UPDATE", ct);
        var recipients = await db.ConversationParticipants.Where(p => p.ConversationId == message.ConversationId && p.LeftAt == null &&
            db.ProjectMembers.Any(m => m.ProjectId == projectId && m.UserId == p.UserId && m.Status == "active")).Select(p => p.UserId).ToArrayAsync(ct);
        await Publish(recipients, "message.created", message);
        await tx.CommitAsync(ct);
    }
    public Task RequestsChangedAsync(IEnumerable<Guid> users, CancellationToken ct) => Publish(users, "chat.requests.changed", new { changed = true });
    public Task ReadAsync(IEnumerable<Guid> users, Guid conversationId, CancellationToken ct) => Publish(users, "conversation.read", new { conversationId });
    private async Task Publish(IEnumerable<Guid> users, string name, object payload)
    {
        try { await hub.Clients.Groups(users.Distinct().Select(ChatHub.UserGroup).ToArray()).SendAsync(name, payload); }
        catch (Exception ex) { logger.LogWarning(ex, "Chat delivery failed; clients resync on reconnect"); }
    }
}