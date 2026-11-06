using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Application.DTOs.Chat;
using Tindy.Application.Entities;
using Tindy.Infrastructure.Persistence;
using Tindy.Tests.Integration;
using Xunit;
namespace Tindy.Tests.Projects;

public sealed class ChatRequestHttpTests(TindyApiFactory factory) : IClassFixture<TindyApiFactory>
{
    private const string Path = "/api/chat/requests";
    private static CreateChatRequest Input(ApplicationCase d, Guid? target = null, Guid? position = null, Guid? application = null) => new(target ?? d.Applicant, d.Project, position ?? d.Position, application, "Let's discuss this project <script>test</script>", Guid.NewGuid());
    private static async Task<JsonElement> Data(HttpResponseMessage r) => await RecruitmentHttp.Data(r);
    private static JsonElement Request(JsonElement r) => r.GetProperty("request");
    private static Guid Id(JsonElement r) => Request(r).GetProperty("request_id").GetGuid();
    private async Task Mutate(Func<TindyDbContext, Task> change) { using var s = factory.Services.CreateScope(); await change(s.ServiceProvider.GetRequiredService<TindyDbContext>()); }
    private static async Task<JsonElement> Act(HttpClient c, Guid id, string action) => await Data(await c.PostAsync($"{Path}/{id}/{action}", null));

    [Fact]
    public async Task Consent_EnforcedForOldRoutes_AndAcceptIsAtomicIdempotent()
    {
        var d = await TeammateTestData.Seed(factory); using var leader = RecruitmentHttp.Client(factory, d.Leader); using var member = RecruitmentHttp.Client(factory, d.Applicant); using var outsider = RecruitmentHttp.Client(factory, d.Outsider);
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.PostAsJsonAsync("/api/chat/conversations/direct", new { user_id = d.Applicant })).StatusCode);
        var application = await ProjectApplicationHttpTests.Apply(member, d.Project, d.Position);
        var input = Input(d, application: application); var created = await Data(await leader.PostAsJsonAsync(Path, input));
        Assert.Equal("application", Request(created).GetProperty("kind").GetString());
        Assert.Equal(application, Request(created).GetProperty("application_id").GetGuid());
        Assert.Empty((await Data(await member.GetAsync("/api/chat/conversations"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsync($"{Path}/{Id(created)}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.PostAsync($"{Path}/{Id(created)}/accept", null)).StatusCode);
        var results = await Task.WhenAll(Act(member, Id(created), "accept"), Act(member, Id(created), "accept"));
        var room = results[0].GetProperty("conversation_id").GetGuid(); Assert.Equal(room, results[1].GetProperty("conversation_id").GetGuid());
        var messages = await Data(await member.GetAsync($"/api/chat/conversations/{room}/messages"));
        var opening = Assert.Single(messages.GetProperty("items").EnumerateArray());
        Assert.Equal(input.Body, opening.GetProperty("body").GetString()); Assert.Equal(d.Project, opening.GetProperty("context_project_id").GetGuid());
        var already = await Data(await leader.PostAsJsonAsync(Path, Input(d))); Assert.Equal(room, already.GetProperty("conversation_id").GetGuid());
        await Mutate(async db => { Assert.Equal("pending", (await db.ProjectApplications.SingleAsync(x => x.ApplicationId == application)).Status); Assert.False(await db.ProjectMembers.AnyAsync(x => x.ProjectId == d.Project && x.UserId == d.Applicant)); Assert.False(await db.ProjectInvitations.AnyAsync(x => x.ProjectId == d.Project)); });
    }
    [Fact]
    public async Task ReverseRequestsAndRetries_ConsumeOneSlot_AndDoNotAutoAccept()
    {
        var d = await TeammateTestData.Seed(factory); using var a = RecruitmentHttp.Client(factory, d.Leader); using var b = RecruitmentHttp.Client(factory, d.Applicant);
        var first = Input(d); var reverse = new CreateChatRequest(d.Leader, d.Project, null, null, "Question", Guid.NewGuid());
        var results = await Task.WhenAll(a.PostAsJsonAsync(Path, first), b.PostAsJsonAsync(Path, reverse));
        var one = await Data(results[0]); var two = await Data(results[1]); Assert.Equal(Id(one), Id(two)); Assert.Equal("pending", Request(one).GetProperty("status").GetString());
        // The winning sender's identical retry has one stable request ID.
        var retry = await Data(await a.PostAsJsonAsync(Path, first)); Assert.Equal(Id(one), Id(retry));
        await Mutate(async db => Assert.Single(await db.ChatRequests.Where(x => x.ProjectId == d.Project).ToListAsync()));
        // Pending consent must not create a DIRECT room; the leader already has a project group.
        Assert.DoesNotContain((await Data(await a.GetAsync("/api/chat/conversations"))).EnumerateArray(), c => c.GetProperty("type").GetString() == "direct");
    }
    [Fact]
    public async Task RejectionAndCancellationAllowImmediateRetry_WithoutQuotaRefund()
    {
        var d = await TeammateTestData.Seed(factory); using var a = RecruitmentHttp.Client(factory, d.Leader); using var b = RecruitmentHttp.Client(factory, d.Applicant);
        for (var i = 0; i < 5; i++) { var r = await Data(await a.PostAsJsonAsync(Path, Input(d))); await Act(i % 2 == 0 ? b : a, Id(r), i % 2 == 0 ? "reject" : "cancel"); }
        var limited = await a.PostAsJsonAsync(Path, Input(d)); Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        var problem = JsonDocument.Parse(await limited.Content.ReadAsStringAsync()).RootElement; Assert.Equal("CHAT_REQUEST_LIMIT", problem.GetProperty("error_code").GetString()); Assert.True(problem.TryGetProperty("retry_at", out _)); Assert.NotNull(limited.Headers.RetryAfter);
        await Mutate(async db => await db.ChatRequests.Where(x => x.SenderId == d.Leader).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, DateTime.UtcNow.AddHours(-2))));
        Assert.True((await a.PostAsJsonAsync(Path, Input(d))).IsSuccessStatusCode);
    }
    [Fact]
    public async Task DailyQuotaAndConcurrentLastSlot_AreAtomic()
    {
        var d = await TeammateTestData.Seed(factory); using var a = RecruitmentHttp.Client(factory, d.Leader);
        await Mutate(async db =>
        {
            for (var i = 0; i < 19; i++) db.ChatRequests.Add(new ChatRequest { RequestId = Guid.NewGuid(), SenderId = d.Leader, RecipientId = d.Applicant, PairKey = "history", ProjectId = d.Project, ProjectName = "History", Kind = "opportunity", Body = "History", Status = "rejected", CreatedAt = DateTime.UtcNow.AddHours(-2), ExpiresAt = DateTime.UtcNow.AddDays(6), ClientRequestId = Guid.NewGuid(), InputKey = "history" });
            await db.SaveChangesAsync();
        });
        var results = await Task.WhenAll(a.PostAsJsonAsync(Path, Input(d)), a.PostAsJsonAsync(Path, Input(d, d.Outsider)));
        Assert.Single(results, x => x.IsSuccessStatusCode); Assert.Single(results, x => x.StatusCode == HttpStatusCode.TooManyRequests);
    }
    [Theory]
    [InlineData("project", "project_closed")]
    [InlineData("deadline", "project_closed")]
    [InlineData("position", "position_closed")]
    [InlineData("expired", "expired")]
    public async Task ContextEnds_RejectsAcceptance_EvenWithoutWorker(string kind, string reason)
    {
        var d = await TeammateTestData.Seed(factory); using var a = RecruitmentHttp.Client(factory, d.Leader); using var b = RecruitmentHttp.Client(factory, d.Applicant);
        var created = await Data(await a.PostAsJsonAsync(Path, Input(d))); var id = Id(created);
        await Mutate(async db =>
        {
            if (kind == "project") await db.Projects.Where(x => x.ProjectId == d.Project).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "in_progress"));
            if (kind == "deadline") await db.Projects.Where(x => x.ProjectId == d.Project).ExecuteUpdateAsync(s => s.SetProperty(x => x.RecruitmentClosesAt, DateTime.UtcNow.AddSeconds(-1)));
            if (kind == "position") await db.ProjectPositions.Where(x => x.PositionId == d.Position).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "filled"));
            if (kind == "expired") await db.ChatRequests.Where(x => x.RequestId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        });
        var accepted = await Act(b, id, "accept"); Assert.NotEqual("accepted", accepted.GetProperty("status").GetString()); Assert.Equal(reason, accepted.GetProperty("end_reason").GetString());
        Assert.Empty((await Data(await b.GetAsync("/api/chat/conversations"))).EnumerateArray());
    }
    [Theory]
    [InlineData("accepted")]
    [InlineData("rejected")]
    [InlineData("withdrawn")]
    public async Task ApplicationEnds_CancelsLinkedRequest_ButNotGeneralQuestion(string status)
    {
        var d = await TeammateTestData.Seed(factory); using var a = RecruitmentHttp.Client(factory, d.Leader); using var b = RecruitmentHttp.Client(factory, d.Applicant);
        var application = await ProjectApplicationHttpTests.Apply(b, d.Project, d.Position);
        var linked = await Data(await a.PostAsJsonAsync(Path, Input(d))); Assert.Equal(application, Request(linked).GetProperty("application_id").GetGuid());
        await Mutate(async db => await db.ProjectApplications.Where(x => x.ApplicationId == application).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status)));
        var history = await Data(await b.GetAsync(Path + "?history=true")); var row = Assert.Single(history.GetProperty("items").EnumerateArray()); Assert.Equal("application_" + status, row.GetProperty("end_reason").GetString());
        var general = await Data(await b.PostAsJsonAsync(Path, new CreateChatRequest(d.Leader, d.Project, null, null, "General question", Guid.NewGuid())));
        Assert.Equal(JsonValueKind.Null, Request(general).GetProperty("application_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, Request(general).GetProperty("position_id").ValueKind);
        await Mutate(async db => await db.ProjectPositions.Where(x => x.PositionId == d.Position).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "closed")));
        Assert.Equal("accepted", (await Act(a, Id(general), "accept")).GetProperty("status").GetString());
    }
    [Fact]
    public async Task BlockingBothWays_PreservesConsentAndHistory_AndNeverRevivesPending()
    {
        var d = await TeammateTestData.Seed(factory); using var a = RecruitmentHttp.Client(factory, d.Leader); using var b = RecruitmentHttp.Client(factory, d.Applicant);
        var pending = await Data(await a.PostAsJsonAsync(Path, Input(d)));
        Assert.True((await b.PutAsync("/api/chat/blocks/" + d.Leader, null)).IsSuccessStatusCode);
        var cancelled = await Act(a, Id(pending), "cancel"); Assert.Equal("cancelled", cancelled.GetProperty("status").GetString()); Assert.Equal("contact_unavailable", cancelled.GetProperty("end_reason").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await a.PostAsJsonAsync(Path, Input(d))).StatusCode);
        await b.DeleteAsync("/api/chat/blocks/" + d.Leader);
        var fresh = await Data(await a.PostAsJsonAsync(Path, Input(d))); Assert.NotEqual(Id(pending), Id(fresh));
        var accepted = await Act(b, Id(fresh), "accept"); var room = accepted.GetProperty("conversation_id").GetGuid();
        await Task.WhenAll(a.PutAsync("/api/chat/blocks/" + d.Applicant, null), b.PutAsync("/api/chat/blocks/" + d.Leader, null));
        await a.DeleteAsync("/api/chat/blocks/" + d.Applicant);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.PostAsJsonAsync($"/api/chat/conversations/{room}/messages", new { body = "Blocked" })).StatusCode);
        await b.DeleteAsync("/api/chat/blocks/" + d.Leader);
        Assert.True((await a.PostAsJsonAsync($"/api/chat/conversations/{room}/messages", new { body = "Unblocked" })).IsSuccessStatusCode);
        await Mutate(async db => await db.Projects.Where(x => x.ProjectId == d.Project).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "archived")));
        Assert.True((await b.PostAsJsonAsync($"/api/chat/conversations/{room}/messages", new { body = "Still available" })).IsSuccessStatusCode);
        var rows = (await Data(await b.GetAsync($"/api/chat/conversations/{room}/messages"))).GetProperty("items"); Assert.Equal(3, rows.GetArrayLength());
    }
    [Fact]
    public async Task ShareContext_UsesExistingRoomWithoutRequestQuota_AndDeduplicatesRetry()
    {
        var d = await TeammateTestData.Seed(factory); using var leader = RecruitmentHttp.Client(factory, d.Leader); using var member = RecruitmentHttp.Client(factory, d.Applicant);
        var created = await Data(await leader.PostAsJsonAsync(Path, Input(d))); var accepted = await Act(member, Id(created), "accept"); var room = accepted.GetProperty("conversation_id").GetGuid();
        var key = Guid.NewGuid(); var input = new { project_id = d.Project, position_id = d.Position, body = "A project card", client_message_id = key };
        var results = await Task.WhenAll(leader.PostAsJsonAsync($"/api/chat/conversations/{room}/context", input), leader.PostAsJsonAsync($"/api/chat/conversations/{room}/context", input));
        var first = await Data(results[0]); var second = await Data(results[1]); Assert.Equal(first.GetProperty("message_id").GetGuid(), second.GetProperty("message_id").GetGuid());
        Assert.Equal(d.Project, first.GetProperty("context_project_id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync($"/api/chat/conversations/{room}/context", new { project_id = d.Project, position_id = d.OtherPosition, body = "A project card", client_message_id = key })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync($"/api/chat/conversations/{room}/messages", new { body = "A project card", client_message_id = key })).StatusCode);
        await Mutate(async db => { Assert.Single(await db.ChatRequests.Where(x => x.ProjectId == d.Project).ToArrayAsync()); Assert.Equal(2, await db.ChatMessages.CountAsync(x => x.ConversationId == room)); });
    }
    [Fact]
    public async Task IdempotencyRejectsDifferentBody_AndAcceptRejectRaceHasOneOutcome()
    {
        var d = await TeammateTestData.Seed(factory); using var a = RecruitmentHttp.Client(factory, d.Leader); using var b = RecruitmentHttp.Client(factory, d.Applicant);
        var input = Input(d); var first = await Data(await a.PostAsJsonAsync(Path, input));
        var retry = await Data(await a.PostAsJsonAsync(Path, input)); Assert.Equal(Id(first), Id(retry));
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsJsonAsync(Path, input with { Body = "Changed" })).StatusCode);
        var results = await Task.WhenAll(Act(b, Id(first), "accept"), Act(b, Id(first), "reject")); Assert.Equal(results[0].GetProperty("status").GetString(), results[1].GetProperty("status").GetString());
        await Mutate(async db => Assert.InRange(await db.ChatMessages.CountAsync(x => x.ContextProjectId == d.Project), 0, 1));
    }
    [Fact]
    public async Task ProjectClosureCommittedFirst_CannotGrantChatAfterWaiting()
    {
        var d = await TeammateTestData.Seed(factory); using var a = RecruitmentHttp.Client(factory, d.Leader); using var b = RecruitmentHttp.Client(factory, d.Applicant);
        var created = await Data(await a.PostAsJsonAsync(Path, Input(d)));
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Projects.Where(x => x.ProjectId == d.Project).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "in_progress"));
        var accept = b.PostAsync($"{Path}/{Id(created)}/accept", null);
        await Task.Delay(100); Assert.False(accept.IsCompleted);
        await tx.CommitAsync();
        var response = await accept;
        if (response.IsSuccessStatusCode) Assert.Equal("cancelled", (await Data(response)).GetProperty("status").GetString());
        else Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("cancelled", await db.ChatRequests.Where(x => x.RequestId == Id(created)).Select(x => x.Status).SingleAsync());
        Assert.Empty((await Data(await b.GetAsync("/api/chat/conversations"))).EnumerateArray());
    }
    [Theory]
    [InlineData("self")]
    [InlineData("wrong_leader")]
    [InlineData("wrong_position")]
    [InlineData("blank")]
    [InlineData("long")]
    [InlineData("inactive")]
    public async Task InvalidRequestsNeverConsumeQuota(string kind)
    {
        var d = await TeammateTestData.Seed(factory); using var a = RecruitmentHttp.Client(factory, d.Leader); using var outsider = RecruitmentHttp.Client(factory, d.Outsider);
        var input = Input(d);
        if (kind == "self") input = input with { UserId = d.Leader };
        if (kind == "wrong_position") input = input with { PositionId = Guid.NewGuid() };
        if (kind == "blank") input = input with { Body = "   " };
        if (kind == "long") input = input with { Body = new string('x', 501) };
        if (kind == "inactive") await Mutate(async db => await db.Users.Where(x => x.UserId == d.Applicant).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "suspended")));
        var response = await (kind == "wrong_leader" ? outsider : a).PostAsJsonAsync(Path, input); Assert.False(response.IsSuccessStatusCode);
        await Mutate(async db => Assert.False(await db.ChatRequests.AnyAsync(x => x.ProjectId == d.Project)));
    }
}

internal static class ChatTestWire
{
    public static Task<HttpResponseMessage> PostAsJsonAsync(this HttpClient client, string url, CreateChatRequest request) =>
        System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(client, url, request, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
}
