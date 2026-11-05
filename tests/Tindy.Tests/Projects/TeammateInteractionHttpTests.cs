using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Infrastructure.Persistence;
using Tindy.Tests.Integration;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class TeammateInteractionHttpTests(TindyApiFactory factory) : IClassFixture<TindyApiFactory>
{
    internal static string Path(ApplicationCase data, Guid? candidate = null, Guid? position = null) =>
        $"/api/projects/{data.Project}/candidate-interactions" + (candidate.HasValue ? $"/{candidate}" : "") + $"?position_id={position ?? data.Position}";

    [Fact]
    public async Task ActionsReplace_ResetIsIdempotent_NoOpPreservesTime_AndScopeIsExplicit()
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader);
        var path = Path(data, data.Applicant);
        var saved = await RecruitmentHttp.Data(await leader.PutAsJsonAsync(path, new { action = "saved" }));
        var noOp = await RecruitmentHttp.Data(await leader.PutAsJsonAsync(path, new { action = "saved" }));
        Assert.Equal(saved.GetProperty("created_at").GetString(), noOp.GetProperty("created_at").GetString());
        Assert.Equal(saved.GetProperty("updated_at").GetString(), noOp.GetProperty("updated_at").GetString());
        var shortlisted = await RecruitmentHttp.Data(await leader.PutAsJsonAsync(path, new { action = "shortlisted" }));
        Assert.Equal("shortlisted", shortlisted.GetProperty("action").GetString());
        await RecruitmentHttp.Data(await leader.PutAsJsonAsync(path, new { action = "skipped" }));
        var feed = await RecruitmentHttp.Data(await leader.GetAsync(TeammateTestData.Feed(data)));
        Assert.DoesNotContain(feed.GetProperty("items").EnumerateArray(), i => i.GetProperty("candidate").GetProperty("user_id").GetGuid() == data.Applicant);
        await RecruitmentHttp.Data(await leader.PutAsJsonAsync(Path(data, data.Applicant, data.OtherPosition), new { action = "saved" }));
        var listing = await RecruitmentHttp.Data(await leader.GetAsync(Path(data) + "&action=skipped&page_size=1"));
        Assert.Single(listing.GetProperty("items").EnumerateArray());
        Assert.False(listing.GetProperty("items")[0].GetProperty("is_discoverable").GetBoolean());
        Assert.Equal(1, listing.GetProperty("pagination").GetProperty("total_items").GetInt32());
        foreach (var unused in Enumerable.Range(0, 2))
        {
            var reset = await RecruitmentHttp.Data(await leader.DeleteAsync(path));
            Assert.Equal(System.Text.Json.JsonValueKind.Null, reset.GetProperty("action").ValueKind);
        }
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        Assert.Single(await db.TeammateDiscoveryInteractions.Where(i => i.ProjectId == data.Project).ToListAsync());
        Assert.Empty(await db.ProjectInvitations.Where(i => i.ProjectId == data.Project).ToListAsync());
        Assert.Equal(1, await db.ProjectMembers.CountAsync(m => m.ProjectId == data.Project));
    }

    [Fact]
    public async Task Ownership_StrictBody_RequiredPosition_AndUnavailablePreview()
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader);
        using var other = RecruitmentHttp.Client(factory, data.Outsider);
        await ProjectApplicationHttpTests.Problem(await other.GetAsync(Path(data)), HttpStatusCode.Forbidden, "PROJECT_ACCESS_DENIED");
        await ProjectApplicationHttpTests.Problem(await leader.GetAsync($"/api/projects/{data.Project}/candidate-interactions"), HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        foreach (var body in new[] { "{}", "{\"action\":\"saved\",\"action\":\"skipped\"}", "{\"action\":\"saved\",\"user_id\":\"spoofed\"}", "{\"action\":\"interested\"}" })
            await ProjectApplicationHttpTests.Problem(await leader.PutAsync(Path(data, data.Applicant), new StringContent(body, Encoding.UTF8, "application/json")), HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await RecruitmentHttp.Data(await leader.PutAsJsonAsync(Path(data, data.Applicant), new { action = "saved" }));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            await db.UserProfiles.Where(p => p.UserId == data.Applicant).ExecuteUpdateAsync(s => s.SetProperty(p => p.OpenToTeamInvitations, false));
            await db.Projects.Where(p => p.ProjectId == data.Project).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "archived").SetProperty(p => p.ArchivedAt, DateTime.UtcNow));
        }
        var listing = await RecruitmentHttp.Data(await leader.GetAsync(Path(data)));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, listing.GetProperty("items")[0].GetProperty("candidate").ValueKind);
        Assert.False(listing.GetProperty("items")[0].GetProperty("is_discoverable").GetBoolean());
        await RecruitmentHttp.Data(await leader.DeleteAsync(Path(data, data.Applicant)));
    }
}
