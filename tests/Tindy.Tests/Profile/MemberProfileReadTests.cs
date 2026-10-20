using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Tindy.Api.Authorization;
using Tindy.Api.ExceptionHandlers;
using Tindy.Api.Controllers;
using Tindy.Application.DTOs.Profile;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Models.Authorization;
using Tindy.Application.Services;
using Xunit;

namespace Tindy.Tests.Profile;

/// <summary>STEP 06: pure unit tests. SQL/HTTP integration remains pending PostgreSQL availability.</summary>
public sealed class MemberProfileReadTests
{
    private static readonly Guid CallerId = Guid.NewGuid();
    private static readonly Guid TargetId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid SkillId = Guid.NewGuid();

    [Fact]
    public async Task Service_UsesRouteTargetId_AndPassesCancellationToken()
    {
        var repo = new FakeProfileRepository(ExampleMemberProfile());
        var service = new ProfileService(repo);
        using var cts = new CancellationTokenSource();

        var data = await service.GetMemberProfileAsync(TargetId, cts.Token);

        Assert.Equal(TargetId, repo.LastRequestedUserId);
        Assert.Equal(cts.Token, repo.LastCancellationToken);
        Assert.Equal(TargetId, data.UserId);
        Assert.Equal("Backend Developer", data.PreferredRole?.Name);
        Assert.Equal(SkillId, Assert.Single(data.Skills).SkillId);
        Assert.Equal(2, data.ExperiencesCount);
        Assert.Equal(1, data.CertificatesCount);
    }

    [Fact]
    public async Task Service_EmptyTargetId_IsNotFoundWithoutRepositoryCall()
    {
        var repo = new FakeProfileRepository(ExampleMemberProfile());
        await Assert.ThrowsAsync<NotFoundException>(() => new ProfileService(repo).GetMemberProfileAsync(Guid.Empty));
        Assert.Null(repo.LastRequestedUserId);
    }

    [Fact]
    public async Task Service_IneligibleOrMissingTarget_IsIndistinguishable404()
    {
        // Repository is contractually responsible for filtering inactive, deleted,
        // not-onboarded and missing targets to null (tested end-to-end later).
        var repo = new FakeProfileRepository(null);
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => new ProfileService(repo).GetMemberProfileAsync(TargetId));
        Assert.Equal("Profile was not found.", ex.Message);
    }

    [Fact]
    public async Task Controller_ReturnsEnvelope_OnlyWhitelistedMemberFields()
    {
        var controller = NewController(ExampleMemberProfile(), CallerId.ToString("D"));
        var ok = Assert.IsType<OkObjectResult>(await controller.GetMemberProfile(TargetId, CancellationToken.None));
        Assert.Equal(200, ok.StatusCode);

        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("Member profile retrieved successfully.", root.GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(TargetId.ToString("D"), data.GetProperty("user_id").GetString());
        Assert.Equal("member-contact@example.com", data.GetProperty("public_email").GetString());
        Assert.Equal(RoleId.ToString("D"), data.GetProperty("preferred_role").GetProperty("professional_role_id").GetString());
        Assert.Equal(SkillId.ToString("D"), data.GetProperty("skills")[0].GetProperty("skill_id").GetString());
        Assert.Equal(2, data.GetProperty("experiences_count").GetInt32());
        Assert.Equal(1, data.GetProperty("certificates_count").GetInt32());
        Assert.False(data.TryGetProperty("experiences", out _));
        Assert.False(data.TryGetProperty("certificates", out _));

        foreach (var privateField in new[] { "phone_number", "resume_url", "primary_email", "primary_email_normalized",
                     "password_hash", "email_verified_at", "user_credentials", "external_auth_accounts",
                     "user_roles", "status", "is_onboarding_completed", "is_public", "deleted_at" })
            Assert.False(data.TryGetProperty(privateField, out _), $"Private field leaked: {privateField}");
    }

    [Fact]
    public async Task MemberRoute_ReturnsSameFilteredSchemaEvenWhenTargetEqualsCaller()
    {
        var model = ExampleMemberProfile() with { UserId = CallerId };
        var controller = NewController(model, CallerId.ToString("D"));
        var ok = Assert.IsType<OkObjectResult>(await controller.GetMemberProfile(CallerId, CancellationToken.None));
        using var parsed = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
        var data = parsed.RootElement.GetProperty("data");
        Assert.Equal(CallerId.ToString("D"), data.GetProperty("user_id").GetString());
        Assert.False(data.TryGetProperty("phone_number", out _));
        Assert.False(data.TryGetProperty("resume_url", out _));
        Assert.False(data.TryGetProperty("primary_email", out _));
    }

    [Fact]
    public void MemberProfileDto_HasNoPrivateProperties()
    {
        var actualNames = typeof(MemberProfileResponse).GetProperties().Select(property => property.Name).ToHashSet();
        foreach (var privateField in new[] { "PhoneNumber", "ResumeUrl", "PrimaryEmail", "PasswordHash",
                     "Credentials", "ExternalAuthAccounts", "Status", "IsOnboardingCompleted", "IsPublic" })
            Assert.DoesNotContain(privateField, actualNames);
    }

    [Fact]
    public async Task MissingTarget_MapsTo404ProblemDetails()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = $"/api/users/{TargetId:D}/profile";
        context.Response.Body = new MemoryStream();
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var ex = new NotFoundException("Profile was not found.");
        Assert.True(await handler.TryHandleAsync(context, ex, CancellationToken.None));
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("NOT_FOUND", document.RootElement.GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Controller_PreservesNullOptionals_AndEmptySkills()
    {
        var model = ExampleMemberProfile() with
        {
            Username = null, PreferredRole = null, PublicEmail = null,
            Skills = Array.Empty<ProfileSkillSummary>(), ExperiencesCount = 0, CertificatesCount = 0
        };
        var controller = NewController(model, CallerId.ToString("D"));
        var response = Assert.IsType<OkObjectResult>(await controller.GetMemberProfile(TargetId, CancellationToken.None));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(response.Value,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("public_email").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("preferred_role").ValueKind);
        Assert.Empty(data.GetProperty("skills").EnumerateArray());
        Assert.Equal(0, data.GetProperty("experiences_count").GetInt32());
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid-sub")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Controller_InvalidCallerClaim_IsRejected(string sub)
    {
        var controller = NewController(ExampleMemberProfile(), sub);
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => controller.GetMemberProfile(TargetId, CancellationToken.None));
    }

    [Fact]
    public void Controller_RequiresDefaultJwtAndCompletedOnboardingPolicy()
    {
        var controllerType = typeof(MemberProfileController);
        Assert.Equal("api/users/{user_id:guid}/profile", controllerType.GetCustomAttribute<RouteAttribute>()?.Template);
        var attribute = Assert.IsType<AuthorizeAttribute>(controllerType.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(attribute.Policy);
        Assert.Null(controllerType.GetCustomAttribute<AllowAnonymousAttribute>());
        var action = controllerType.GetMethod(nameof(MemberProfileController.GetMemberProfile))!;
        Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());
        Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
        // The default policy in ServiceCollectionExtensions uses OnboardingCompletedRequirement.
    }

    [Theory]
    [InlineData(OnboardingAccessStatus.Completed, true)]
    [InlineData(OnboardingAccessStatus.Incomplete, false)]
    [InlineData(OnboardingAccessStatus.InactiveOrMissing, false)]
    public async Task CallerAuthorization_RejectsIncompleteAndInactiveAccounts(OnboardingAccessStatus status, bool allowed)
    {
        var reader = new FakeAccessReader(status);
        var requirement = new OnboardingCompletedRequirement();
        var context = new AuthorizationHandlerContext(new[] { requirement }, ValidPrincipal(), new DefaultHttpContext());
        await new OnboardingCompletedHandler(reader).HandleAsync(context);
        Assert.Equal(allowed, context.HasSucceeded);
        Assert.Equal(CallerId, reader.ReadFor);
    }

    [Fact]
    public async Task CallerAuthorization_RejectsAnonymousWithoutDatabaseAccess()
    {
        var reader = new FakeAccessReader(OnboardingAccessStatus.Completed);
        var requirement = new OnboardingCompletedRequirement();
        var context = new AuthorizationHandlerContext(new[] { requirement }, new ClaimsPrincipal(new ClaimsIdentity()), new DefaultHttpContext());
        await new OnboardingCompletedHandler(reader).HandleAsync(context);
        Assert.False(context.HasSucceeded);
        Assert.Null(reader.ReadFor);
    }

    private static MemberProfileController NewController(MemberProfileResponse? model, string sub)
    {
        var claims = string.IsNullOrEmpty(sub) ? Array.Empty<Claim>() : new[] { new Claim("sub", sub) };
        return new MemberProfileController(new ProfileService(new FakeProfileRepository(model)))
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

    private static ClaimsPrincipal ValidPrincipal() => new(new ClaimsIdentity(
        new[] { new Claim("sub", CallerId.ToString("D")) }, "Bearer"));

    private static MemberProfileResponse ExampleMemberProfile() => new(
        TargetId, "alexle", "Alex Le", "FPT University", "Backend Developer", null,
        "Ho Chi Minh City", "Student", new PreferredRoleSummary(RoleId, "Backend Developer"), 10,
        "Cloud", null, null, null, "https://github.com/example", null, null,
        "member-contact@example.com", null, null,
        new[] { new ProfileSkillSummary(SkillId, ".NET") }, 2, 1);

    private sealed class FakeProfileRepository(MemberProfileResponse? response) : IProfileRepository
    {
        public Guid? LastRequestedUserId { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<MemberProfileResponse?> GetMemberProfileAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            LastRequestedUserId = userId;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(response);
        }

        public Task<MyProfileResponse?> GetMyProfileAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<User?> GetUserForUpdateAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> IsActiveProfessionalRoleAsync(Guid roleId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<Guid>> GetActiveSkillIdsAsync(IReadOnlyCollection<Guid> skillIds, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public void SyncUserSkills(UserProfile profile, IReadOnlyCollection<Guid> skillIds) => throw new NotImplementedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<T> ExecuteInTransactionAsync<T>(Guid userId, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeAccessReader(OnboardingAccessStatus status) : IOnboardingAccessReader
    {
        public Guid? ReadFor { get; private set; }
        public Task<OnboardingAccessStatus> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            ReadFor = userId;
            return Task.FromResult(status);
        }
    }
}
