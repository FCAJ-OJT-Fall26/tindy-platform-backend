using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tindy.Api.Controllers;
using Tindy.Application.DTOs.Profile;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Services;
using Xunit;

namespace Tindy.Tests.Profile;

/// <summary>STEP 04 unit tests: no PostgreSQL, Docker or Testcontainers.</summary>
public sealed class MyProfileReadTests
{
    private static readonly Guid ValidUserId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid SkillId = Guid.NewGuid();

    [Fact]
    public async Task ProfileService_ReturnsProjectionAndPassesUserIdAndCancellationToken()
    {
        var expected = ExampleProfile();
        var repo = new FakeProfileRepository(expected);
        var service = new ProfileService(repo);
        using var cts = new CancellationTokenSource();

        var actual = await service.GetMyProfileAsync(ValidUserId, cts.Token);

        Assert.Same(expected, actual);
        Assert.Equal(ValidUserId, repo.LastUserId);
        Assert.Equal(cts.Token, repo.LastCancellationToken);
        Assert.Equal("Backend Developer", actual.PreferredRole?.Name);
        Assert.Equal(SkillId, Assert.Single(actual.Skills).SkillId);
        Assert.Equal(2, actual.ExperiencesCount);
        Assert.Equal(1, actual.CertificatesCount);
    }

    [Fact]
    public async Task ProfileService_PreservesNullableValuesForOAuthOnlyAccount()
    {
        var nullable = ExampleProfile() with
        {
            Username = null, PhoneNumber = null, PreferredRole = null,
            Bio = null, ResumeUrl = null, Skills = Array.Empty<ProfileSkillSummary>(),
            ExperiencesCount = 0, CertificatesCount = 0
        };

        var actual = await new ProfileService(new FakeProfileRepository(nullable)).GetMyProfileAsync(ValidUserId);

        Assert.Null(actual.Username);
        Assert.Null(actual.PreferredRole);
        Assert.Empty(actual.Skills);
        Assert.Equal(0, actual.CertificatesCount);
    }

    [Fact]
    public async Task ProfileService_UnknownOrIncompleteProfileReturnsNotFound()
    {
        var repo = new FakeProfileRepository(null);
        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => new ProfileService(repo).GetMyProfileAsync(ValidUserId));
        Assert.Equal("Profile was not found.", exception.Message);
    }

    [Fact]
    public async Task ProfileService_EmptyUserIdRejectsWithoutDatabaseRead()
    {
        var repo = new FakeProfileRepository(ExampleProfile());
        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => new ProfileService(repo).GetMyProfileAsync(Guid.Empty));
        Assert.Null(repo.LastUserId);
    }

    [Fact]
    public async Task Controller_ReturnsConventionalEnvelope_ForJwtSub()
    {
        var service = new ProfileService(new FakeProfileRepository(ExampleProfile()));
        var controller = NewController(service, ValidUserId.ToString("D"));
        var response = await controller.GetMyProfile(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(response);
        var json = JsonSerializer.Serialize(ok.Value,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("User profile retrieved successfully.", root.GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(ValidUserId.ToString("D"), data.GetProperty("user_id").GetString());
        Assert.Equal(RoleId.ToString("D"), data.GetProperty("preferred_role").GetProperty("professional_role_id").GetString());
        Assert.Equal(SkillId.ToString("D"), data.GetProperty("skills")[0].GetProperty("skill_id").GetString());
        Assert.Equal(2, data.GetProperty("experiences_count").GetInt32());
        Assert.Equal(1, data.GetProperty("certificates_count").GetInt32());
        Assert.False(data.TryGetProperty("primary_email", out _));
        Assert.False(data.TryGetProperty("credential", out _));
        Assert.False(data.TryGetProperty("external_auth_accounts", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Controller_MissingOrInvalidSub_IsRejected(string sub)
    {
        var controller = NewController(new ProfileService(new FakeProfileRepository(ExampleProfile())), sub);
        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => controller.GetMyProfile(CancellationToken.None));
    }

    [Fact]
    public void Controller_UsesDefaultAuthorizationPolicy_NotPreOnboardingPolicy()
    {
        var route = typeof(MyProfileController).GetCustomAttribute<RouteAttribute>();
        var authorize = typeof(MyProfileController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("api/me/profile", route?.Template);
        Assert.NotNull(authorize);
        Assert.Null(authorize.Policy);
        Assert.Null(typeof(MyProfileController).GetCustomAttribute<AllowAnonymousAttribute>());
        var method = typeof(MyProfileController).GetMethod(nameof(MyProfileController.GetMyProfile))!;
        Assert.NotNull(method.GetCustomAttribute<HttpGetAttribute>());
    }

    private static MyProfileController NewController(IProfileService service, string sub)
    {
        var claims = string.IsNullOrEmpty(sub) ? Array.Empty<Claim>() : new[] { new Claim("sub", sub) };
        return new MyProfileController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
                }
            }
        };
    }

    private static MyProfileResponse ExampleProfile() => new(
        ValidUserId, "alexle", "Alex Le", "FPT University", "0900000000",
        "Backend Developer", null, "Ho Chi Minh City", "Student",
        new PreferredRoleSummary(RoleId, "Backend Developer"), 10,
        "Cloud", null, null, null, "https://github.com/example", null,
        null, null, null, null, null,
        new[] { new ProfileSkillSummary(SkillId, ".NET") }, 2, 1);

    private sealed class FakeProfileRepository(MyProfileResponse? profile) : IProfileRepository
    {
        public Guid? LastUserId { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<MyProfileResponse?> GetMyProfileAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            LastUserId = userId;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(profile);
        }
        public Task<Tindy.Application.Entities.User?> GetUserForUpdateAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> IsActiveProfessionalRoleAsync(Guid roleId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<Guid>> GetActiveSkillIdsAsync(IReadOnlyCollection<Guid> skillIds, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public void SyncUserSkills(Tindy.Application.Entities.UserProfile profile, IReadOnlyCollection<Guid> skillIds) => throw new NotImplementedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<T> ExecuteInTransactionAsync<T>(Guid userId, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
