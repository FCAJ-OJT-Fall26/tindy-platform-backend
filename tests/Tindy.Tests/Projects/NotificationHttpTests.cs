using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Application.Entities;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Repositories;
using Tindy.Tests.Integration;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class NotificationHttpTests(TindyApiFactory factory) : IClassFixture<TindyApiFactory>
{
    private const string Path = "/api/me/notifications";

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("inactive", HttpStatusCode.Forbidden)]
    [InlineData("incomplete", HttpStatusCode.Forbidden)]
    public async Task AllEndpoints_RequireActiveAccountAndOnboarding(string state, HttpStatusCode expected)
    {
        var data = await TeammateTestData.Seed(factory);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        if (state == "inactive") await db.Users.Where(u => u.UserId == data.Applicant).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, "suspended"));
        if (state == "incomplete") await db.Users.Where(u => u.UserId == data.Applicant).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsOnboardingCompleted, false));
        using var client = state == "anonymous" ? factory.CreateHttpsClient() : RecruitmentHttp.Client(factory, data.Applicant);
        Assert.Equal(expected, (await client.GetAsync(Path)).StatusCode);
        Assert.Equal(expected, (await client.GetAsync(Path + "/unread-count")).StatusCode);
        Assert.Equal(expected, (await client.PatchAsync($"{Path}/{Guid.NewGuid()}/read", null)).StatusCode);
        Assert.Equal(expected, (await client.PostAsync(Path + "/mark-all-read", null)).StatusCode);
    }

    [Theory]
    [InlineData("filter=read")]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("page_size=10")]
    [InlineData("page_size=0")]
    [InlineData("recipient_user_id=00000000-0000-0000-0000-000000000001")]
    [InlineData("filter=all&filter=unread")]
    [InlineData("filter=")]
    public async Task UnsupportedQueries_ReturnValidationError(string query)
    {
        var data = await TeammateTestData.Seed(factory);
        using var member = RecruitmentHttp.Client(factory, data.Applicant);
        await ProjectApplicationHttpTests.Problem(await member.GetAsync(Path + "?" + query), HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task List_IsRecipientScoped_PaginatesDeterministically_AndFiltersUnread()
    {
        var data = await TeammateTestData.Seed(factory);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var now = DateTime.UtcNow.AddMinutes(-1);
        var ids = new List<Guid>();
        // Equal timestamps force the notification UUID tie-breaker to matter.
        for (var i = 1; i <= 23; i++)
        {
            var invitation = TeammateTestData.Invitation(data); invitation.Status = "cancelled";
            db.ProjectInvitations.Add(invitation);
            var id = Guid.Parse($"00000000-0000-0000-0000-{i:D12}"); ids.Add(id);
            db.Notifications.Add(Row(invitation.InvitationId, data.Applicant, now, id: id, readAt: i % 2 == 0 ? now : null));
        }
        var other = TeammateTestData.Invitation(data, data.Outsider); db.ProjectInvitations.Add(other);
        db.Notifications.Add(Row(other.InvitationId, data.Outsider, now));
        await db.SaveChangesAsync();
        using var member = RecruitmentHttp.Client(factory, data.Applicant);
        var first = await RecruitmentHttp.Data(await member.GetAsync(Path));
        var second = await RecruitmentHttp.Data(await member.GetAsync(Path + "?page=2&page_size=20"));
        var rows = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).ToArray();
        Assert.Equal(ids.AsEnumerable().Reverse(), rows.Select(r => r.GetProperty("notification_id").GetGuid()));
        Assert.Equal(20, first.GetProperty("items").GetArrayLength());
        Assert.Equal(3, second.GetProperty("items").GetArrayLength());
        Assert.Equal(23, first.GetProperty("pagination").GetProperty("total_items").GetInt32());
        Assert.True(first.GetProperty("pagination").GetProperty("has_next").GetBoolean());
        Assert.True(second.GetProperty("pagination").GetProperty("has_previous").GetBoolean());
        var unread = await RecruitmentHttp.Data(await member.GetAsync(Path + "?filter=unread"));
        Assert.Equal(12, unread.GetProperty("items").GetArrayLength());
        Assert.All(unread.GetProperty("items").EnumerateArray(), row => Assert.False(row.GetProperty("is_read").GetBoolean()));
        Assert.Equal(12, (await RecruitmentHttp.Data(await member.GetAsync(Path + "/unread-count"))).GetProperty("unread_count").GetInt32());
        var empty = await RecruitmentHttp.Data(await member.GetAsync(Path + "?page=2147483647"));
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Read_IsIdempotentUnderConcurrency_PreservesTimestampAndHistory_AndHidesOtherRecipients()
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader); using var member = RecruitmentHttp.Client(factory, data.Applicant);
        var invitationId = await ProjectInvitationHttpTests.Send(leader, data);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var id = await db.Notifications.Where(n => n.InvitationId == invitationId).Select(n => n.NotificationId).SingleAsync();
        using var outsider = RecruitmentHttp.Client(factory, data.Outsider);
        await ProjectApplicationHttpTests.Problem(await outsider.PatchAsync($"{Path}/{id}/read", null), HttpStatusCode.NotFound, "NOTIFICATION_NOT_FOUND");
        await ProjectApplicationHttpTests.Problem(await member.PatchAsync($"{Path}/{Guid.NewGuid()}/read", null), HttpStatusCode.NotFound, "NOTIFICATION_NOT_FOUND");
        Assert.Null(await db.Notifications.Where(n => n.NotificationId == id).Select(n => n.ReadAt).SingleAsync());
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => member.PatchAsync($"{Path}/{id}/read", null)));
        var rows = await Task.WhenAll(responses.Select(RecruitmentHttp.Data));
        var readAt = rows[0].GetProperty("read_at").GetDateTime();
        Assert.All(rows, row => { Assert.True(row.GetProperty("is_read").GetBoolean()); Assert.Equal(readAt, row.GetProperty("read_at").GetDateTime()); });
        var repeated = await RecruitmentHttp.Data(await member.PatchAsync($"{Path}/{id}/read", null));
        Assert.Equal(readAt, repeated.GetProperty("read_at").GetDateTime());
        Assert.Single((await RecruitmentHttp.Data(await member.GetAsync(Path))).GetProperty("items").EnumerateArray());
        Assert.Equal(0, (await RecruitmentHttp.Data(await member.GetAsync(Path + "/unread-count"))).GetProperty("unread_count").GetInt32());
        Assert.Equal(readAt, await db.Notifications.Where(n => n.NotificationId == id).Select(n => n.ReadAt).SingleAsync());
    }

    [Fact]
    public async Task MarkAllRead_IsScoped_Idempotent_PreservesReadAt_AndHonorsCutoff()
    {
        var data = await TeammateTestData.Seed(factory);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var invitation = TeammateTestData.Invitation(data); db.ProjectInvitations.Add(invitation);
        var cutoff = DateTime.UtcNow.AddMinutes(-2); var oldRead = cutoff.AddDays(-1);
        var unread = Row(invitation.InvitationId, data.Applicant, cutoff.AddMinutes(-1));
        var read = Row(invitation.InvitationId, data.Applicant, cutoff.AddMinutes(-1), "invitation_cancelled", readAt: oldRead);
        var future = Row(invitation.InvitationId, data.Applicant, cutoff.AddMinutes(1), "invitation_accepted");
        var other = Row(invitation.InvitationId, data.Outsider, cutoff.AddMinutes(-1));
        db.Notifications.AddRange(unread, read, future, other); await db.SaveChangesAsync();
        var repo = new NotificationRepository(db);
        Assert.Equal(1, await repo.MarkAllReadAsync(data.Applicant, cutoff, default));
        Assert.Equal(0, await repo.MarkAllReadAsync(data.Applicant, cutoff, default));
        db.ChangeTracker.Clear();
        // Compare database-rounded values (PostgreSQL stores microsecond precision).
        Assert.Equal(Truncate(cutoff), (await db.Notifications.SingleAsync(n => n.NotificationId == unread.NotificationId)).ReadAt);
        Assert.Equal(Truncate(oldRead), (await db.Notifications.SingleAsync(n => n.NotificationId == read.NotificationId)).ReadAt);
        Assert.Null((await db.Notifications.SingleAsync(n => n.NotificationId == future.NotificationId)).ReadAt);
        Assert.Null((await db.Notifications.SingleAsync(n => n.NotificationId == other.NotificationId)).ReadAt);
        using var member = RecruitmentHttp.Client(factory, data.Applicant);
        Assert.Equal(1, (await RecruitmentHttp.Data(await member.PostAsync(Path + "/mark-all-read", null))).GetProperty("updated_count").GetInt32());
        Assert.Equal(0, (await RecruitmentHttp.Data(await member.PostAsync(Path + "/mark-all-read", null))).GetProperty("updated_count").GetInt32());
        Assert.Equal(4, await db.Notifications.CountAsync(n => n.InvitationId == invitation.InvitationId));
    }

    [Fact]
    public async Task MarkAllRead_ExcludesBackdatedNotificationCommittedAfterUpdateBegins()
    {
        var data = await TeammateTestData.Seed(factory);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var invitation = TeammateTestData.Invitation(data); db.ProjectInvitations.Add(invitation);
        var old = Row(invitation.InvitationId, data.Applicant, DateTime.UtcNow.AddHours(-1));
        db.Notifications.Add(old); await db.SaveChangesAsync();
        await using var blocker = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT notification_id FROM notifications WHERE notification_id={old.NotificationId} FOR UPDATE");
        using var writerScope = factory.Services.CreateScope(); var writerDb = writerScope.ServiceProvider.GetRequiredService<TindyDbContext>();
        using var insertScope = factory.Services.CreateScope(); var insertDb = insertScope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var update = new NotificationRepository(writerDb).MarkAllReadAsync(data.Applicant, DateTime.UtcNow, default);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!await insertDb.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE '%UPDATE%notifications%') AS \"Value\"").SingleAsync(timeout.Token))
                await Task.Delay(20, timeout.Token);
            var late = Row(invitation.InvitationId, data.Applicant, DateTime.UtcNow.AddHours(-1), "invitation_cancelled");
            insertDb.Notifications.Add(late); await insertDb.SaveChangesAsync();
            await blocker.CommitAsync();
            Assert.Equal(1, await update);
            Assert.Null(await insertDb.Notifications.AsNoTracking().Where(n => n.NotificationId == late.NotificationId).Select(n => n.ReadAt).SingleAsync());
        }
        finally { await blocker.DisposeAsync(); await update; }
    }

    internal static Notification Row(Guid invitation, Guid recipient, DateTime created, string type = "invitation_received", Guid? id = null, DateTime? readAt = null) => new()
    {
        NotificationId = id ?? Guid.NewGuid(), InvitationId = invitation, RecipientUserId = recipient, Type = type,
        Payload = JsonSerializer.SerializeToElement(new { project_name = "Safe Project", position_title = "Developer", actor_display_name = "Member" }),
        CreatedAt = created, ReadAt = readAt
    };
    private static DateTime Truncate(DateTime value) => new(value.Ticks / 10 * 10, DateTimeKind.Utc);
}
