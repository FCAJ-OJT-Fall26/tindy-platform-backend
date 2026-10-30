using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Authorization;
using Tindy.Infrastructure.Persistence;
using Tindy.Tests.Integration;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class DiscoveryHttpTests : IClassFixture<TindyApiFactory>
{
    private readonly TindyApiFactory _factory;
    public DiscoveryHttpTests(TindyApiFactory factory) => _factory = factory;

    [Fact]
    public async Task SaveSkip_AreIdempotentAccountScoped_AndSavedArchiveIsExplicit()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var leader = RecruitmentData.User(); var a = RecruitmentData.User(); var b = RecruitmentData.User();
        db.Users.AddRange(leader, a, b);
        var role = await db.ProfessionalRoles.Select(r => r.ProfessionalRoleId).FirstAsync();
        var category = Guid.NewGuid().ToString();
        var project = RecruitmentData.Project(leader.UserId, role, category);
        db.Projects.Add(project); await db.SaveChangesAsync();
        using var clientA = RecruitmentHttp.Client(_factory, a.UserId);
        using var clientB = RecruitmentHttp.Client(_factory, b.UserId);
        var saved = $"/api/me/discovery/saved/{project.ProjectId}";
        var skipped = $"/api/me/discovery/skipped/{project.ProjectId}";
        foreach (var _ in Enumerable.Range(0, 2)) Assert.Equal(HttpStatusCode.OK, (await clientA.PutAsync(saved, null)).StatusCode);
        // Concurrent updates of different flags must preserve both.
        var responses = await Task.WhenAll(clientA.PutAsync(saved, null), clientA.PutAsync(skipped, null));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var list = await RecruitmentHttp.Data(await clientA.GetAsync("/api/me/discovery/saved"));
        Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.True(list.GetProperty("items")[0].GetProperty("is_skipped").GetBoolean());
        Assert.Empty((await RecruitmentHttp.Data(await clientB.GetAsync("/api/me/discovery/saved"))).GetProperty("items").EnumerateArray());
        Assert.Empty((await RecruitmentHttp.Data(await clientA.GetAsync($"/api/discovery/projects?category={category}"))).GetProperty("items").EnumerateArray());
        Assert.Single((await RecruitmentHttp.Data(await clientB.GetAsync($"/api/discovery/projects?category={category}"))).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await clientA.DeleteAsync(skipped)).StatusCode);
        var feed = await RecruitmentHttp.Data(await clientA.GetAsync($"/api/discovery/projects?category={category}"));
        Assert.True(feed.GetProperty("items")[0].GetProperty("is_saved").GetBoolean());
        Assert.Equal("rule-v1", feed.GetProperty("items")[0].GetProperty("algorithm_version").GetString());
        Assert.True(feed.GetProperty("items")[0].GetProperty("data_incomplete").GetBoolean());
        await db.Projects.Where(p => p.ProjectId == project.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "archived").SetProperty(p => p.ArchivedAt, DateTime.UtcNow));
        list = await RecruitmentHttp.Data(await clientA.GetAsync("/api/me/discovery/saved"));
        Assert.Equal("archived", list.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.False(list.GetProperty("items")[0].GetProperty("is_recruitment_open").GetBoolean());
        foreach (var _ in Enumerable.Range(0, 2)) Assert.Equal(HttpStatusCode.OK, (await clientA.DeleteAsync(saved)).StatusCode);
        Assert.Empty((await RecruitmentHttp.Data(await clientA.GetAsync("/api/me/discovery/saved"))).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await clientA.PutAsync($"/api/me/discovery/saved/{Guid.NewGuid()}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clientA.DeleteAsync($"/api/me/discovery/saved/{Guid.NewGuid()}")).StatusCode);
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("incomplete", HttpStatusCode.Forbidden)]
    [InlineData("inactive", HttpStatusCode.Forbidden)]
    public async Task NewRoutes_UseExistingJwtActiveAccountOnboardingPolicies(string state, HttpStatusCode expected)
    {
        using var app = new OAuthHttpApiFactory(false);
        app.Accounts.Status = state == "inactive" ? OnboardingAccessStatus.InactiveOrMissing : OnboardingAccessStatus.Incomplete;
        using var client = app.CreateHttpsClient();
        if (state != "anonymous") client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.TokenFor());
        var project = Guid.NewGuid(); var application = Guid.NewGuid();
        foreach (var (method, path) in new[]
        {
            (HttpMethod.Get, "/api/discovery/projects"), (HttpMethod.Get, "/api/me/discovery/saved"),
            (HttpMethod.Put, $"/api/me/discovery/saved/{project}"), (HttpMethod.Delete, $"/api/me/discovery/saved/{project}"),
            (HttpMethod.Put, $"/api/me/discovery/skipped/{project}"), (HttpMethod.Delete, $"/api/me/discovery/skipped/{project}"),
            (HttpMethod.Post, $"/api/projects/{project}/applications"), (HttpMethod.Get, $"/api/projects/{project}/applications"),
            (HttpMethod.Post, $"/api/projects/{project}/applications/{application}/accept"),
            (HttpMethod.Post, $"/api/projects/{project}/applications/{application}/reject"),
            (HttpMethod.Get, "/api/me/project-applications"), (HttpMethod.Post, $"/api/me/project-applications/{application}/withdraw")
        })
        {
            using var request = new HttpRequestMessage(method, path);
            using var response = await client.SendAsync(request);
            Assert.Equal(expected, response.StatusCode);
        }
    }

    [Fact]
    public async Task InvalidCursorAndCatalogUuid_ReturnValidationProblem()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var user = RecruitmentData.User(); db.Users.Add(user); await db.SaveChangesAsync();
        using var client = RecruitmentHttp.Client(_factory, user.UserId);
        foreach (var query in new[] { "cursor=bad", "role_id=bad", "skill_id=bad" })
        {
            using var response = await client.GetAsync("/api/discovery/projects?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }
}

internal static class RecruitmentHttp
{
    public static HttpClient Client(TindyApiFactory factory, Guid userId)
    {
        var client = factory.CreateHttpsClient();
        using var scope = factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateAccessToken(userId, ["Member"]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }
    public static async Task<JsonElement> Data(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, raw);
        Assert.DoesNotContain("private-canary", raw);
        Assert.DoesNotContain("primary_email", raw);
        Assert.DoesNotContain("phone_number", raw);
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.GetProperty("data").Clone();
    }
}
