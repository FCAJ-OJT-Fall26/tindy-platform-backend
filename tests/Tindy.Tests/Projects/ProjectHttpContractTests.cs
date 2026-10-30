using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Authorization;
using Xunit;

namespace Tindy.Tests.Projects;

// Uses real controllers, services, JWT validation and policies with an in-memory
// repository. Database persistence is not established by these HTTP checks.
public sealed class ProjectHttpContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProjectResponses_KeepPublicLeaderFields_AndOptionalPositionDescription(bool withDescription)
    {
        using var factory = new ProjectApiFactory();
        using var leader = factory.CreateHttpsClient(authenticated: true);
        using var anonymous = factory.CreateHttpsClient();
        var request = ProjectServiceTests.CreateValidRequest();
        request = request with
        {
            Positions = new[] { request.Positions[0] with { Description = withDescription ? "  Develop REST APIs using .NET  " : null } }
        };

        using var created = await leader.PostAsJsonAsync("/api/projects", request, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createData = await ReadDataAsync(created);
        AssertPublicLeader(createData.GetProperty("leader"));
        Assert.Equal(1, createData.GetProperty("current_team_size").GetInt32());
        var projectId = createData.GetProperty("project_id").GetGuid();
        var saved = Assert.Single(factory.Repository.Projects);
        Assert.Equal(1, saved.InitialTeamSize);
        Assert.True(Assert.Single(saved.Members).IsLeader);

        using var detail = await anonymous.GetAsync($"/api/projects/{projectId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailData = await ReadDataAsync(detail);
        AssertPublicLeader(detailData.GetProperty("leader"));
        Assert.Equal(withDescription ? "Develop REST APIs using .NET" : null,
            detailData.GetProperty("positions")[0].GetProperty("description").GetString());
        Assert.Equal(1, detailData.GetProperty("current_team_size").GetInt32());

        using var list = await anonymous.GetAsync("/api/projects");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listData = await ReadDataAsync(list);
        AssertPublicLeader(listData.GetProperty("items")[0].GetProperty("leader"));

        var updateRequest = new UpdateProjectRequest("Updated Project", null, request.Description, request.Category,
            request.DifficultyLevel, request.ExpectedCommitmentHours, request.Duration, request.MaxTeamCapacity, null, request.Goals);
        using var updated = await leader.PutAsJsonAsync($"/api/projects/{projectId}", updateRequest, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        AssertPublicLeader((await ReadDataAsync(updated)).GetProperty("leader"));
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("incomplete", HttpStatusCode.Forbidden)]
    [InlineData("inactive", HttpStatusCode.Forbidden)]
    public async Task ProjectWriteRoutes_EnforceExistingAccountAndOnboardingPolicies(string account, HttpStatusCode expected)
    {
        using var factory = new ProjectApiFactory();
        factory.Status = account == "inactive" ? OnboardingAccessStatus.InactiveOrMissing : OnboardingAccessStatus.Incomplete;
        using var client = factory.CreateHttpsClient(authenticated: account != "anonymous");
        var projectId = Guid.NewGuid();
        var positionId = Guid.NewGuid();

        foreach (var (method, path) in new[]
        {
            (HttpMethod.Post, "/api/projects"),
            (HttpMethod.Put, $"/api/projects/{projectId}"),
            (HttpMethod.Delete, $"/api/projects/{projectId}"),
            (HttpMethod.Post, $"/api/projects/{projectId}/positions"),
            (HttpMethod.Put, $"/api/projects/{projectId}/positions/{positionId}"),
            (HttpMethod.Delete, $"/api/projects/{projectId}/positions/{positionId}"),
            (HttpMethod.Post, $"/api/projects/{projectId}/positions/{positionId}/skills"),
            (HttpMethod.Delete, $"/api/projects/{projectId}/positions/{positionId}/skills/{Guid.NewGuid()}"),
            (HttpMethod.Get, "/api/me/projects")
        })
        {
            using var message = new HttpRequestMessage(method, path);
            if (method == HttpMethod.Post || method == HttpMethod.Put) message.Content = JsonContent.Create(new { });
            using var response = await client.SendAsync(message);
            Assert.Equal(expected, response.StatusCode);
            if (account == "incomplete")
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("ONBOARDING_REQUIRED", json.GetProperty("error_code").GetString());
            }
        }

        Assert.Empty(factory.Repository.Projects);
    }

    [Fact]
    public async Task CreateProject_InvalidInitialTeamSize_ReturnsClearValidationError()
    {
        using var factory = new ProjectApiFactory();
        using var client = factory.CreateHttpsClient(authenticated: true);

        using var response = await client.PostAsJsonAsync("/api/projects",
            ProjectServiceTests.CreateValidRequest() with { InitialTeamSize = 2 }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VALIDATION_FAILED", problem.GetProperty("error_code").GetString());
        Assert.Contains("Initial team size must be 1", problem.GetProperty("detail").GetString());
        Assert.Empty(factory.Repository.Projects);
    }

    [Theory]
    [InlineData("role_id")]
    [InlineData("skill_id")]
    public async Task CreateProject_MalformedCatalogUuid_IsRejectedWithoutSubstitution(string field)
    {
        using var factory = new ProjectApiFactory();
        using var client = factory.CreateHttpsClient(authenticated: true);
        var payload = JsonSerializer.SerializeToNode(ProjectServiceTests.CreateValidRequest(), JsonOptions)!;
        var target = field == "role_id" ? payload["positions"]![0]! : payload["positions"]![0]!["skills"]![0]!;
        target[field] = "not-a-uuid";

        using var response = await client.PostAsJsonAsync("/api/projects", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Repository.Projects);
    }

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-leader@example.com", raw);
        using var document = JsonDocument.Parse(raw);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("message").GetString()));
        return document.RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task DeadlineContract_RoundTripsDateAndUtcCutoff_AndPreservesOmittedEdit()
    {
        using var factory = new ProjectApiFactory();
        using var leader = factory.CreateHttpsClient(authenticated: true);
        using var anonymous = factory.CreateHttpsClient();
        var date = new DateOnly(2026, 11, 12);
        using var created = await leader.PostAsJsonAsync("/api/projects",
            ProjectServiceTests.CreateValidRequest() with { RecruitmentClosingDate = date }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var data = await ReadDataAsync(created);
        var id = data.GetProperty("project_id").GetGuid();
        AssertDeadline(data, "2026-11-12", "2026-11-12T17:00:00Z", true);

        using var detail = await anonymous.GetAsync($"/api/projects/{id}");
        AssertDeadline(await ReadDataAsync(detail), "2026-11-12", "2026-11-12T17:00:00Z", true);
        using var list = await anonymous.GetAsync("/api/projects");
        AssertDeadline((await ReadDataAsync(list)).GetProperty("items")[0], "2026-11-12", "2026-11-12T17:00:00Z", true);

        using var omittedEdit = await leader.PutAsJsonAsync($"/api/projects/{id}", RecruitmentDeadlineTests.EditRequest(), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, omittedEdit.StatusCode);
        AssertDeadline(await ReadDataAsync(omittedEdit), "2026-11-12", "2026-11-12T17:00:00Z", true);

        factory.Clock.UtcNow = DateTimeOffset.Parse("2026-11-12T17:00:00Z");
        using var expired = await anonymous.GetAsync($"/api/projects/{id}");
        var expiredData = await ReadDataAsync(expired);
        AssertDeadline(expiredData, "2026-11-12", "2026-11-12T17:00:00Z", false);
        Assert.Equal("recruiting", expiredData.GetProperty("status").GetString());

        using var extended = await leader.PutAsJsonAsync($"/api/projects/{id}",
            RecruitmentDeadlineTests.EditRequest(new DateOnly(2026, 11, 14)), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, extended.StatusCode);
        AssertDeadline(await ReadDataAsync(extended), "2026-11-14", "2026-11-14T17:00:00Z", true);
    }

    [Theory]
    [InlineData("2026-11-12T00:00:00")]
    [InlineData("12/11/2026")]
    [InlineData("2026-02-30")]
    [InlineData("2026-10-30")]
    [InlineData("9999-12-31")]
    public async Task InvalidClosingDate_CreateAndEdit_ReturnProblemDetails(string invalid)
    {
        using var factory = new ProjectApiFactory();
        using var leader = factory.CreateHttpsClient(authenticated: true);
        var payload = JsonSerializer.SerializeToNode(ProjectServiceTests.CreateValidRequest(), JsonOptions)!;
        payload["recruitment_closing_date"] = invalid;
        using var create = await leader.PostAsJsonAsync("/api/projects", payload);
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal("VALIDATION_FAILED", (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error_code").GetString());
        Assert.Empty(factory.Repository.Projects);

        using var validCreate = await leader.PostAsJsonAsync("/api/projects", ProjectServiceTests.CreateValidRequest(), JsonOptions);
        var id = (await ReadDataAsync(validCreate)).GetProperty("project_id").GetGuid();
        var originalCutoff = Assert.Single(factory.Repository.Projects).RecruitmentClosesAt;
        var edit = JsonSerializer.SerializeToNode(RecruitmentDeadlineTests.EditRequest(), JsonOptions)!;
        edit["recruitment_closing_date"] = invalid;
        using var update = await leader.PutAsJsonAsync($"/api/projects/{id}", edit);
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        Assert.Equal("VALIDATION_FAILED", (await update.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error_code").GetString());
        Assert.Equal(originalCutoff, Assert.Single(factory.Repository.Projects).RecruitmentClosesAt);
    }

    [Fact]
    public async Task LegacyCreatePayload_StillDefaultsTo14CalendarDays()
    {
        using var factory = new ProjectApiFactory();
        using var leader = factory.CreateHttpsClient(authenticated: true);
        var payload = JsonSerializer.SerializeToNode(ProjectServiceTests.CreateValidRequest(), JsonOptions)!;
        payload["recruitment_closing_days"] = 14;
        using var response = await leader.PostAsJsonAsync("/api/projects", payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        AssertDeadline(await ReadDataAsync(response), "2026-11-13", "2026-11-13T17:00:00Z", true);
    }

    private static void AssertDeadline(JsonElement data, string date, string utc, bool open)
    {
        Assert.Equal(date, data.GetProperty("recruitment_closing_date").GetString());
        Assert.Equal(DateTimeOffset.Parse(utc), data.GetProperty("recruitment_closes_at").GetDateTimeOffset());
        Assert.Equal(open, data.GetProperty("is_recruitment_open").GetBoolean());
        Assert.True(data.GetProperty("closing_days_left").GetInt32() >= 0);
        Assert.False(data.TryGetProperty("recruitment_closing_days", out _));
    }

    private static void AssertPublicLeader(JsonElement leader)
    {
        Assert.Equal(new[] { "avatar_url", "display_name", "initials", "user_id" },
            leader.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        Assert.Equal("Leader Name", leader.GetProperty("display_name").GetString());
        Assert.Equal("https://example.com/avatar.png", leader.GetProperty("avatar_url").GetString());
        Assert.Equal("LN", leader.GetProperty("initials").GetString());
    }

    private sealed class ProjectApiFactory : WebApplicationFactory<Program>, IOnboardingAccessReader
    {
        private readonly Guid _userId = Guid.NewGuid();
        private readonly string _jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        public ProjectServiceTests.FakeProjectRepository Repository { get; } = new();
        public OnboardingAccessStatus Status { get; set; } = OnboardingAccessStatus.Completed;
        public ProjectTestClock Clock { get; } = new(RecruitmentDeadlineTests.Utc("2026-10-30T05:00:00Z"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            var settings = new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "Tindy.ProjectTests",
                ["Jwt:Audience"] = "Tindy.ProjectTests.Client",
                ["Jwt:SigningKey"] = _jwtKey,
                ["Jwt:AccessTokenLifetimeMinutes"] = "60",
                ["Jwt:ClockSkewSeconds"] = "0",
                ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Database=unused_project_tests;Username=unused",
                ["OAuth:Enabled"] = "false",
                ["MediaLifecycle:Enabled"] = "false",
                ["GuardDuty:Enabled"] = "false"
            };
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<IProjectRepository>();
                services.RemoveAll<IOnboardingAccessReader>();
                services.AddSingleton<IProjectRepository>(Repository);
                services.AddSingleton<IOnboardingAccessReader>(this);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
            });
        }

        public HttpClient CreateHttpsClient(bool authenticated = false)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            if (authenticated)
            {
                using var scope = Services.CreateScope();
                var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateAccessToken(_userId, ["Member"]);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            }
            return client;
        }

        public Task<OnboardingAccessStatus> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(Status);
    }
}
