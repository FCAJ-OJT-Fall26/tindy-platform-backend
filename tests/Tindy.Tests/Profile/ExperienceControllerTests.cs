using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tindy.Api.Controllers;
using Tindy.Application.DTOs.Profile;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Services;
using Xunit;

namespace Tindy.Tests.Profile;

// Controller contract/unit tests; real PostgreSQL/JWT end-to-end testing is deferred.
public sealed class ExperienceControllerTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Target = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid ExperienceId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid SkillId = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly JsonSerializerOptions SnakeCase = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private const string ValidBody = """
    {
      "project_name": " RAG Chatbot ",
      "role_title": "Backend Developer",
      "description": "Implemented a RAG API",
      "repo_url": "https://github.com/example/rag",
      "start_date": "2025-09",
      "end_date": null,
      "is_ongoing": true,
      "skill_ids": []
    }
    """;

    private static ExperienceResponse Example() => new(ExperienceId, "RAG Chatbot", "Backend Developer",
        "Implemented a RAG API", [new ExperienceTechnologyResponse(SkillId, ".NET")],
        "https://github.com/example/rag", "2025-09", null, true,
        new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc), null);

    private static T Attach<T>(T controller, string? sub = null) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        if (sub is not null)
            controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", sub)], "test"));
        return controller;
    }

    private static JsonDocument SerializeEnvelope(object? value) =>
        JsonDocument.Parse(JsonSerializer.Serialize(value, SnakeCase));

    [Fact]
    public async Task MyList_Returns200WithSnakeCaseEnvelope_AndEmptyCollection()
    {
        var fake = new StubExperienceService { Results = [] };
        var controller = Attach(new MyExperiencesController(fake), Owner.ToString("D"));

        var ok = Assert.IsType<OkObjectResult>(await controller.List(CancellationToken.None));
        Assert.Equal(200, ok.StatusCode);
        Assert.Equal(Owner, fake.LastCaller);
        using var json = SerializeEnvelope(ok.Value);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Experiences retrieved successfully.", json.RootElement.GetProperty("message").GetString());
        Assert.Empty(json.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task Create_UsesJwtSub_ParsesBody_Returns201WithSafeDto()
    {
        var fake = new StubExperienceService();
        var controller = Attach(new MyExperiencesController(fake), Owner.ToString("D"));
        using var body = JsonDocument.Parse(ValidBody);
        using var cts = new CancellationTokenSource();

        var created = Assert.IsType<ObjectResult>(await controller.Create(body.RootElement, cts.Token));
        Assert.Equal(201, created.StatusCode);
        Assert.Equal(Owner, fake.LastCaller);
        Assert.Equal(cts.Token, fake.LastToken);
        Assert.Equal("RAG Chatbot", fake.LastUpsert?.ProjectName);
        Assert.Equal(new DateOnly(2025, 9, 1), fake.LastUpsert?.StartDate);
        Assert.Null(fake.LastUpsert?.EndDate);
        using var response = SerializeEnvelope(created.Value);
        var data = response.RootElement.GetProperty("data");
        Assert.Equal(ExperienceId.ToString("D"), data.GetProperty("experience_id").GetString());
        Assert.Equal("2025-09", data.GetProperty("start_date").GetString());
        Assert.Equal(".NET", data.GetProperty("technologies")[0].GetProperty("name").GetString());
        Assert.False(data.TryGetProperty("user_id", out _));
        Assert.False(data.TryGetProperty("skill_ids", out _));
    }

    [Fact]
    public async Task Put_UsesRouteId_FullReplacement_AndReturns200()
    {
        var fake = new StubExperienceService();
        var controller = Attach(new MyExperiencesController(fake), Owner.ToString("D"));
        using var body = JsonDocument.Parse(ValidBody);

        var ok = Assert.IsType<OkObjectResult>(await controller.Update(ExperienceId, body.RootElement, CancellationToken.None));
        Assert.Equal(200, ok.StatusCode);
        Assert.Equal(Owner, fake.LastCaller);
        Assert.Equal(ExperienceId, fake.LastExperienceId);
        Assert.Empty(fake.LastUpsert!.SkillIds);
        using var json = SerializeEnvelope(ok.Value);
        Assert.Equal("Experience updated successfully.", json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Delete_UsesRouteId_Returns204WithNoBody()
    {
        var fake = new StubExperienceService();
        var controller = Attach(new MyExperiencesController(fake), Owner.ToString("D"));
        var result = Assert.IsType<NoContentResult>(await controller.Delete(ExperienceId, CancellationToken.None));
        Assert.Equal(204, result.StatusCode);
        Assert.Equal(Owner, fake.LastCaller);
        Assert.Equal(ExperienceId, fake.LastExperienceId);
    }

    [Theory]
    [InlineData("{\"project_name\":\"Demo\",\"user_id\":\"bad\"}")]
    [InlineData("{\"project_name\":\"Demo\",\"role_title\":\"Dev\",\"description\":\"Hello\",\"skill_ids\":[],\"is_ongoing\":false,\"start_date\":\"2025-13\"}")]
    [InlineData("{\"project_name\":\"Demo\",\"role_title\":\"Dev\",\"description\":\"Hello\",\"skill_ids\":[],\"is_ongoing\":false,\"technologies\":[]}")]
    public async Task PostAndPut_RejectInvalidBodyBeforeServiceMutation(string badJson)
    {
        var fake = new StubExperienceService();
        var controller = Attach(new MyExperiencesController(fake), Owner.ToString("D"));
        using var body = JsonDocument.Parse(badJson);
        await Assert.ThrowsAsync<ProfileValidationException>(() => controller.Create(body.RootElement, CancellationToken.None));
        await Assert.ThrowsAsync<ProfileValidationException>(() => controller.Update(ExperienceId, body.RootElement, CancellationToken.None));
        Assert.Equal(0, fake.Mutations);
    }

    [Fact]
    public async Task ForeignExperience_Throws404FromService_OnUpdateAndDelete()
    {
        var fake = new StubExperienceService { RejectForeign = true };
        var controller = Attach(new MyExperiencesController(fake), Owner.ToString("D"));
        using var body = JsonDocument.Parse(ValidBody);
        await Assert.ThrowsAsync<NotFoundException>(() => controller.Update(ExperienceId, body.RootElement, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => controller.Delete(ExperienceId, CancellationToken.None));
    }

    [Fact]
    public async Task MemberList_UsesRouteTarget_NotJwtSub_AndReturnsOnlyExperiences()
    {
        var fake = new StubExperienceService { Results = [Example()] };
        var controller = Attach(new MemberExperiencesController(fake), Owner.ToString("D"));
        var ok = Assert.IsType<OkObjectResult>(await controller.List(Target, CancellationToken.None));
        Assert.Equal(Target, fake.LastTarget);
        Assert.NotEqual(Owner, fake.LastTarget);
        using var json = SerializeEnvelope(ok.Value);
        var data = json.RootElement.GetProperty("data");
        Assert.Single(data.EnumerateArray());
        Assert.Equal("RAG Chatbot", data[0].GetProperty("project_name").GetString());
        foreach (var field in new[] { "phone_number", "primary_email", "resume_url", "password_hash", "external_auth_accounts" })
            Assert.False(data[0].TryGetProperty(field, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task CallerWithoutValidSub_IsRejected(string? sub)
    {
        var service = new StubExperienceService();
        var mine = Attach(new MyExperiencesController(service), sub);
        var member = Attach(new MemberExperiencesController(service), sub);
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => mine.List(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => member.List(Target, CancellationToken.None));
        Assert.Null(service.LastCaller);
        Assert.Null(service.LastTarget);
    }

    [Fact]
    public void Controllers_UseDefaultAuthorizationPolicyAndExactRoutes()
    {
        var mine = typeof(MyExperiencesController);
        var member = typeof(MemberExperiencesController);
        foreach (var type in new[] { mine, member })
        {
            var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorize);
            Assert.Null(authorize!.Policy); // DefaultPolicy enforces JWT + active + completed onboarding.
            Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());
        }

        Assert.Equal("api/me/profile/experiences", mine.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("api/users/{user_id:guid}/profile/experiences", member.GetCustomAttribute<RouteAttribute>()!.Template);
        var mineMethods = mine.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        var memberMethods = member.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.Single(mineMethods, method => method.GetCustomAttribute<HttpGetAttribute>() is not null);
        Assert.Single(mineMethods, method => method.GetCustomAttribute<HttpPostAttribute>() is not null);
        Assert.Equal("{experience_id:guid}", mine.GetMethod("Update")!.GetCustomAttribute<HttpPutAttribute>()!.Template);
        Assert.Equal("{experience_id:guid}", mine.GetMethod("Delete")!.GetCustomAttribute<HttpDeleteAttribute>()!.Template);
        Assert.Single(memberMethods, method => method.GetCustomAttribute<HttpGetAttribute>() is not null);
        Assert.DoesNotContain(memberMethods, method =>
            method.GetCustomAttribute<HttpPutAttribute>() is not null ||
            method.GetCustomAttribute<HttpDeleteAttribute>() is not null ||
            method.GetCustomAttribute<HttpPostAttribute>() is not null);
    }

    private sealed class StubExperienceService : IExperienceService
    {
        public IReadOnlyList<ExperienceResponse> Results { get; init; } = [Example()];
        public bool RejectForeign { get; init; }
        public Guid? LastCaller { get; private set; }
        public Guid? LastTarget { get; private set; }
        public Guid? LastExperienceId { get; private set; }
        public ExperienceUpsertRequest? LastUpsert { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public int Mutations { get; private set; }

        public Task<IReadOnlyList<ExperienceResponse>> ListMyExperiencesAsync(Guid callerUserId, CancellationToken token = default)
        {
            LastCaller = callerUserId;
            return Task.FromResult(Results);
        }

        public Task<IReadOnlyList<ExperienceResponse>> ListMemberExperiencesAsync(Guid targetUserId, CancellationToken token = default)
        {
            LastTarget = targetUserId;
            return Task.FromResult(Results);
        }

        public Task<ExperienceResponse> GetMyExperienceAsync(Guid callerUserId, Guid experienceId, CancellationToken token = default)
            => Task.FromResult(Example());

        public Task<ExperienceResponse> CreateAsync(Guid callerUserId, ExperienceUpsertRequest request, CancellationToken token = default)
        {
            LastCaller = callerUserId; LastUpsert = request; LastToken = token; Mutations++;
            return Task.FromResult(Example());
        }

        public Task<ExperienceResponse> UpdateAsync(Guid callerUserId, Guid experienceId, ExperienceUpsertRequest request, CancellationToken token = default)
        {
            LastCaller = callerUserId; LastExperienceId = experienceId; LastUpsert = request; Mutations++;
            if (RejectForeign) throw new NotFoundException("Experience was not found.");
            return Task.FromResult(Example());
        }

        public Task DeleteAsync(Guid callerUserId, Guid experienceId, CancellationToken token = default)
        {
            LastCaller = callerUserId; LastExperienceId = experienceId; Mutations++;
            if (RejectForeign) throw new NotFoundException("Experience was not found.");
            return Task.CompletedTask;
        }
    }
}
