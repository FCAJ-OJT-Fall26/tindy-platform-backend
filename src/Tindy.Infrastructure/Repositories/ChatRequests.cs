using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tindy.Application.DTOs.Chat;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Services;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

public sealed class ChatRequests(TindyDbContext db, IChatNotifier notifier, TimeProvider clock) : IChatRequests
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public static string Pair(Guid a, Guid b) => string.Join(":", new[] { a.ToString("N"), b.ToString("N") }.Order(StringComparer.Ordinal));
    private static ChatRequestException Error(int status, string code, string message) => new(status, code, message);
    private Task Lock(string key, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
    private async Task Available(Guid a, Guid b, CancellationToken ct)
    {
        if (a == b || a == Guid.Empty || b == Guid.Empty) throw Error(400, "VALIDATION_FAILED", "Choose another account.");
        if (await db.Users.CountAsync(u => (u.UserId == a || u.UserId == b) && u.Status == "active" && u.DeletedAt == null && u.IsOnboardingCompleted, ct) != 2 ||
            await db.ChatBlocks.AnyAsync(x => x.PairKey == Pair(a, b), ct))
            throw Error(403, "CHAT_CONTACT_UNAVAILABLE", "This contact is unavailable.");
    }

    public async Task<ChatRequestResult> CreateAsync(Guid user, CreateChatRequest input, CancellationToken ct)
    {
        var body = input.Body?.Trim();
        if (string.IsNullOrEmpty(body) || body.Length > 500 || input.ClientRequestId == Guid.Empty || input.ProjectId == Guid.Empty)
            throw Error(400, "VALIDATION_FAILED", "A project, request ID and opening message of 1–500 characters are required.");
        var key = Pair(user, input.UserId);
        var inputKey = JsonSerializer.Serialize(new { input.UserId, input.ProjectId, input.PositionId, input.ApplicationId, Body = body });
        await SweepAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Sender quota before pair lock: reverse-direction creates cannot deadlock.
        await Lock($"chat-quota:{user:N}", ct);
        await Lock(key, ct);
        await Available(user, input.UserId, ct);
        var retry = await db.ChatRequests.AsNoTracking().SingleOrDefaultAsync(x => x.SenderId == user && x.ClientRequestId == input.ClientRequestId, ct);
        if (retry != null)
        {
            if (retry.InputKey != inputKey) throw Error(409, "CHAT_IDEMPOTENCY_CONFLICT", "Request ID was used for different content.");
            await tx.CommitAsync(ct);
            return new(await Dto(retry, user, ct), retry.ConversationId);
        }
        var room = await db.Conversations.AsNoTracking().SingleOrDefaultAsync(x => x.DirectKey == key && x.Status == "active" && x.ConsentSource != null, ct);
        if (room != null) { await tx.CommitAsync(ct); return new(null, room.ConversationId); }
        var pending = await db.ChatRequests.AsNoTracking().SingleOrDefaultAsync(x => x.PairKey == key && x.Status == "pending", ct);
        if (pending != null) { await tx.CommitAsync(ct); return new(await Dto(pending, user, ct), null); }

        var context = await Context(user, input, ct);
        var now = Now;
        var recent = await db.ChatRequests.AsNoTracking().Where(x => x.SenderId == user && x.CreatedAt > now.AddHours(-24)).OrderBy(x => x.CreatedAt).Select(x => x.CreatedAt).ToArrayAsync(ct);
        var hour = recent.Where(x => x > now.AddHours(-1)).ToArray();
        var retryAt = now;
        if (hour.Length >= 5) retryAt = hour[hour.Length - 5].AddHours(1);
        if (recent.Length >= 20) retryAt = new[] { retryAt, recent[recent.Length - 20].AddHours(24) }.Max();
        if (retryAt > now) throw new ChatRequestException(429, "CHAT_REQUEST_LIMIT", "Request limit reached. Please try again later.", retryAt);
        var request = new ChatRequest
        {
            RequestId = Guid.NewGuid(),
            SenderId = user,
            RecipientId = input.UserId,
            PairKey = key,
            ProjectId = context.Project.ProjectId,
            ProjectName = context.Project.Name,
            PositionId = context.Position?.PositionId,
            PositionTitle = context.Position?.Title,
            ApplicationId = context.Application?.ApplicationId,
            Kind = context.Kind,
            Body = body,
            CreatedAt = now,
            ExpiresAt = now.AddDays(7),
            ClientRequestId = input.ClientRequestId,
            InputKey = inputKey
        };
        db.ChatRequests.Add(request);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifier.RequestsChangedAsync([user, input.UserId], ct);
        return new(await Dto(request, user, ct), null);
    }

    private async Task<(Project Project, ProjectPosition? Position, ProjectApplication? Application, string Kind)> Context(Guid user, CreateChatRequest input, CancellationToken ct)
    {
        // Lock in the same parent-to-child order used by recruitment mutations.
        var project = await db.Projects.FromSqlInterpolated($"SELECT * FROM projects WHERE project_id = {input.ProjectId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (project == null || !RecruitmentDeadline.IsOpen(project, Now)) throw Error(409, "CHAT_CONTEXT_CLOSED", "Project is no longer recruiting.");
        var leaderSending = project.LeaderId == user;
        if (!leaderSending && input.UserId != project.LeaderId) throw Error(403, "CHAT_ACTION_FORBIDDEN", "The other person must lead this project.");
        ProjectPosition? position = null;
        if (input.PositionId.HasValue)
        {
            position = await db.ProjectPositions.FromSqlInterpolated($"SELECT * FROM project_positions WHERE position_id = {input.PositionId.Value} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
            if (position == null || position.ProjectId != project.ProjectId) throw Error(400, "VALIDATION_FAILED", "Position does not belong to this project.");
            if (position.Status != "open" || position.FilledCount >= position.RequiredCount) throw Error(409, "CHAT_CONTEXT_CLOSED", "Position is no longer recruiting.");
        }
        var applicant = leaderSending ? input.UserId : user;
        ProjectApplication? application = null;
        if (input.ApplicationId.HasValue)
        {
            application = await db.ProjectApplications.FromSqlInterpolated($"SELECT * FROM project_applications WHERE application_id = {input.ApplicationId.Value} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
            if (application == null || application.ProjectId != project.ProjectId || application.ApplicantId != applicant || position == null || application.PositionId != position.PositionId)
                throw Error(400, "VALIDATION_FAILED", "Application does not match this person and position.");
            if (application.Status != "pending") throw Error(409, "CHAT_CONTEXT_CLOSED", "Application has ended.");
        }
        else if (position != null)
        {
            application = await db.ProjectApplications.AsNoTracking().FirstOrDefaultAsync(x => x.ProjectId == project.ProjectId && x.PositionId == position.PositionId && x.ApplicantId == applicant && x.Status == "pending", ct);
            if (application != null)
                application = await db.ProjectApplications.FromSqlInterpolated($"SELECT * FROM project_applications WHERE application_id = {application.ApplicationId} FOR UPDATE").AsNoTracking().SingleAsync(ct);
            if (application?.Status != null && application.Status != "pending") throw Error(409, "CHAT_CONTEXT_CLOSED", "Application has ended.");
        }
        if (leaderSending && position == null) throw Error(400, "VALIDATION_FAILED", "Choose a recruiting position.");
        return (project, position, application, application != null ? "application" : leaderSending ? "opportunity" : "project_question");
    }

    public async Task<ChatRequestDto> ActAsync(Guid user, Guid requestId, string action, CancellationToken ct)
    {
        if (action is not ("accept" or "reject" or "cancel")) throw Error(400, "VALIDATION_FAILED", "Unsupported request action.");
        await SweepAsync(ct);
        var initial = await db.ChatRequests.AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == requestId && (x.SenderId == user || x.RecipientId == user), ct)
            ?? throw Error(404, "CHAT_REQUEST_NOT_FOUND", "Request not found.");
        if (action == "cancel" ? initial.SenderId != user : initial.RecipientId != user) throw Error(403, "CHAT_ACTION_FORBIDDEN", "This action is not available.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Lock(initial.PairKey, ct);
        if (initial.Status == "pending" && action == "accept")
        {
            await Available(initial.SenderId, initial.RecipientId, ct);
            await Context(initial.SenderId, new(initial.RecipientId, initial.ProjectId, initial.PositionId, initial.ApplicationId, initial.Body, initial.ClientRequestId), ct);
        }
        // A project mutation may have cancelled the request while its row lock was acquired above.
        var request = await db.ChatRequests.FromSqlInterpolated($"SELECT * FROM chat_requests WHERE request_id = {requestId} FOR UPDATE").SingleAsync(ct);
        if (request.Status == "pending" && request.ExpiresAt <= Now) { request.Status = "expired"; request.EndReason = "expired"; request.RespondedAt = Now; }
        if (request.Status == "pending")
        {
            request.Status = action == "accept" ? "accepted" : action == "reject" ? "rejected" : "cancelled";
            request.EndReason = action == "cancel" ? "sender_cancelled" : null;
            request.RespondedAt = Now;
            if (action == "accept")
            {
                var room = await db.Conversations.SingleOrDefaultAsync(x => x.DirectKey == request.PairKey && x.Status == "active", ct);
                if (room == null)
                {
                    room = new Conversation { ConversationId = Guid.NewGuid(), DirectKey = request.PairKey, CreatedAt = Now, UpdatedAt = Now };
                    room.Participants.Add(new() { UserId = request.SenderId, JoinedAt = Now });
                    room.Participants.Add(new() { UserId = request.RecipientId, JoinedAt = Now });
                    db.Conversations.Add(room);
                }
                room.ConsentSource = "request";
                var opening = new ChatMessage
                {
                    MessageId = Guid.NewGuid(),
                    ConversationId = room.ConversationId,
                    SenderId = request.SenderId,
                    Body = request.Body,
                    CreatedAt = Now,
                    ContextProjectId = request.ProjectId,
                    ContextProjectName = request.ProjectName,
                    ContextPositionTitle = request.PositionTitle
                };
                db.ChatMessages.Add(opening);
                room.LastMessageAt = opening.CreatedAt; room.UpdatedAt = opening.CreatedAt;
                request.ConversationId = room.ConversationId;
            }
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await notifier.RequestsChangedAsync([request.SenderId, request.RecipientId], ct);
        return await Dto(request, user, ct);
    }

    public async Task SetBlockAsync(Guid user, Guid target, bool blocked, CancellationToken ct)
    {
        if (user == target || target == Guid.Empty) throw Error(400, "VALIDATION_FAILED", "Choose another account.");
        if (!await db.Users.AnyAsync(x => x.UserId == target, ct)) throw Error(404, "CHAT_REQUEST_NOT_FOUND", "Account not found.");
        var key = Pair(user, target);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Lock(key, ct);
        var entry = await db.ChatBlocks.SingleOrDefaultAsync(x => x.BlockerId == user && x.BlockedId == target, ct);
        if (blocked && entry == null) db.ChatBlocks.Add(new() { BlockerId = user, BlockedId = target, PairKey = key, CreatedAt = Now });
        if (!blocked && entry != null) db.ChatBlocks.Remove(entry);
        if (blocked) await db.ChatRequests.Where(x => x.PairKey == key && x.Status == "pending").ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "cancelled").SetProperty(x => x.EndReason, "blocked").SetProperty(x => x.RespondedAt, Now), ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await notifier.RequestsChangedAsync([user, target], ct);
    }

    public async Task<ChatRequestsPage> ListAsync(Guid user, string direction, bool history, int page, CancellationToken ct)
    {
        if (direction is not ("received" or "sent") || page < 1 || page > 10000) throw Error(400, "VALIDATION_FAILED", "Invalid direction or page.");
        await SweepAsync(ct);
        var query = db.ChatRequests.AsNoTracking().Where(x => direction == "received" ? x.RecipientId == user : x.SenderId == user);
        if (!history) query = query.Where(x => x.Status == "pending");
        var rows = await query.OrderByDescending(x => x.Status == "pending").ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.RequestId).Skip((page - 1) * 30).Take(31).ToListAsync(ct);
        var items = new List<ChatRequestDto>(); foreach (var row in rows.Take(30)) items.Add(await Dto(row, user, ct));
        var count = await db.ChatRequests.CountAsync(x => x.RecipientId == user && x.Status == "pending", ct);
        return new(items, count, rows.Count > 30);
    }
    public async Task<IReadOnlyList<ChatBlockDto>> BlocksAsync(Guid user, CancellationToken ct) =>
        await (from b in db.ChatBlocks
               where b.BlockerId == user
               join u in db.Users on b.BlockedId equals u.UserId
               select new ChatBlockDto(u.UserId, u.Profile != null ? u.Profile.DisplayName : u.Username ?? "User", b.CreatedAt)).ToArrayAsync(ct);

    public async Task<IReadOnlyList<ChatContextOption>> ContextsAsync(Guid user, Guid? target, CancellationToken ct)
    {
        if (!target.HasValue) throw Error(400, "VALIDATION_FAILED", "Choose a contact first.");
        await Available(user, target.Value, ct);
        var now = Now;
        var projects = await db.Projects.AsNoTracking().Where(p => (p.LeaderId == user || p.LeaderId == target.Value) && p.Status == "recruiting" && p.ArchivedAt == null && p.RecruitmentClosesAt > now).OrderByDescending(p => p.CreatedAt).Take(100).ToArrayAsync(ct);
        var options = new List<ChatContextOption>();
        var other = await PublicUser(target.Value, ct);
        foreach (var p in projects)
        {
            if (p.LeaderId == target.Value) options.Add(new(target.Value, other.DisplayName, p.ProjectId, p.Name, null, null, null));
            var positions = await db.ProjectPositions.AsNoTracking().Where(x => x.ProjectId == p.ProjectId && x.Status == "open" && x.FilledCount < x.RequiredCount).ToArrayAsync(ct);
            foreach (var position in positions)
            {
                var applicant = p.LeaderId == user ? target.Value : user;
                var application = await db.ProjectApplications.AsNoTracking().Where(x => x.ProjectId == p.ProjectId && x.PositionId == position.PositionId && x.ApplicantId == applicant && x.Status == "pending").Select(x => (Guid?)x.ApplicationId).FirstOrDefaultAsync(ct);
                options.Add(new(target.Value, other.DisplayName, p.ProjectId, p.Name, position.PositionId, position.Title, application));
            }
        }
        return options;
    }
    private async Task<ChatUserDto> PublicUser(Guid id, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().Include(x => x.Profile).SingleOrDefaultAsync(x => x.UserId == id, ct);
        return new(id, u?.Profile?.DisplayName ?? u?.Username ?? "User", u?.Username, u?.Profile?.AvatarUrl);
    }
    private async Task<ChatRequestDto> Dto(ChatRequest r, Guid viewer, CancellationToken ct) =>
        new(r.RequestId, r.SenderId, r.RecipientId, await PublicUser(viewer == r.SenderId ? r.RecipientId : r.SenderId, ct), r.ProjectId, r.ProjectName, r.PositionId, r.PositionTitle, r.ApplicationId, r.Kind, r.Body, r.Status,
            r.EndReason == "blocked" && viewer == r.SenderId ? "contact_unavailable" : r.EndReason, r.CreatedAt, r.ExpiresAt, r.RespondedAt, r.ConversationId);

    public async Task SweepAsync(CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT chat_refresh_requests({Now})", ct);
    }

    public async Task<ChatMessageDto> ShareContextAsync(Guid user, Guid conversationId, ShareChatContextRequest input, CancellationToken ct)
    {
        var body = input.Body?.Trim();
        if (string.IsNullOrEmpty(body) || body.Length > 500 || input.ClientMessageId == Guid.Empty)
            throw Error(400, "VALIDATION_FAILED", "A message of 1–500 characters and message ID are required.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var room = await db.Conversations.Include(x => x.Participants).SingleOrDefaultAsync(x => x.ConversationId == conversationId && x.Status == "active" && x.ConsentSource != null && x.Participants.Any(p => p.UserId == user), ct)
            ?? throw Error(404, "CHAT_REQUEST_NOT_FOUND", "Conversation not found.");
        var other = room.Participants.Single(x => x.UserId != user).UserId;
        await Lock(Pair(user, other), ct);
        await Available(user, other, ct);
        var inputKey = JsonSerializer.Serialize(new { input.ProjectId, input.PositionId, Body = body });
        await Lock($"{user:N}:{input.ClientMessageId:N}", ct);
        var existing = await db.ChatMessages.AsNoTracking().SingleOrDefaultAsync(x => x.SenderId == user && x.ClientMessageId == input.ClientMessageId, ct);
        if (existing != null)
        {
            if (existing.ConversationId != conversationId || existing.Body != body || existing.ContextInputKey != inputKey)
                throw Error(409, "CHAT_IDEMPOTENCY_CONFLICT", "Message ID was used for different content.");
            await tx.CommitAsync(ct);
            return MessageDto(existing);
        }
        var context = await Context(user, new(other, input.ProjectId, input.PositionId, null, body, input.ClientMessageId), ct);
        var message = new ChatMessage
        {
            MessageId = Guid.NewGuid(),
            ConversationId = conversationId,
            SenderId = user,
            Body = body,
            CreatedAt = Now,
            ClientMessageId = input.ClientMessageId,
            ContextProjectId = input.ProjectId,
            ContextProjectName = context.Project.Name,
            ContextPositionTitle = context.Position?.Title, ContextInputKey = inputKey
        };
        db.ChatMessages.Add(message);
        room.LastMessageAt = message.CreatedAt; room.UpdatedAt = message.CreatedAt;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        var dto = MessageDto(message);
        await notifier.MessageCreatedAsync([user, other], dto, ct);
        return dto;
    }
    private static ChatMessageDto MessageDto(ChatMessage m) => new(m.MessageId, m.ConversationId, m.SenderId, m.Body, m.CreatedAt, m.ClientMessageId, m.ContextProjectId, m.ContextProjectName, m.ContextPositionTitle);
}
