using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Tindy.Api.Controllers;
using Tindy.Api.ExceptionHandlers;
using Tindy.Application.DTOs.Profile;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Services;
using Xunit;

namespace Tindy.Tests.Profile;

public sealed class ProfilePartialUpdateTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid SkillId = Guid.NewGuid();
    private static readonly Guid InactiveSkillId = Guid.NewGuid();
    private static readonly Guid AddedSkillId = Guid.NewGuid();

    [Fact]
    public void Parser_DistinguishesMissingNullAndNewValue()
    {
        var req = Parse("""{"bio":null,"learning_goals":"  New goal  "}""");
        Assert.False(req.Headline.IsSpecified);
        Assert.True(req.Bio.IsSpecified);
        Assert.Null(req.Bio.Value);
        Assert.Equal("New goal", req.LearningGoals.Value);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"username\":\"hacker\"}")]
    [InlineData("{\"avatar_url\":\"https://example.com/a.png\"}")]
    [InlineData("{\"primary_email\":\"test@example.com\"}")]
    [InlineData("{\"is_onboarding_completed\":false}")]
    [InlineData("{\"bio\":\"first\",\"bio\":\"second\"}")]
    [InlineData("{\"display_name\":null}")]
    [InlineData("{\"university\":\"   \"}")]
    [InlineData("{\"available_hours_per_week\":0}")]
    [InlineData("{\"available_hours_per_week\":41}")]
    [InlineData("{\"available_hours_per_week\":2.5}")]
    [InlineData("{\"preferred_role_id\":null}")]
    [InlineData("{\"skill_ids\":null}")]
    [InlineData("{\"skill_ids\":[]}")]
    [InlineData("{\"skill_ids\":[\"00000000-0000-0000-0000-000000000000\"]}")]
    [InlineData("{\"github_url\":\"ftp://example.com\"}")]
    [InlineData("{\"public_email\":\"invalid\"}")]
    [InlineData("{\"bio\":123}")]
    public void Parser_RejectsInvalidRestrictedAndAmbiguousPayloads(string json)
    {
        Assert.Throws<ProfileValidationException>(() => Parse(json));
    }

    [Fact]
    public void Parser_RejectsDuplicateAndTooManySkills()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ProfileValidationException>(() => Parse($"{{\"skill_ids\":[\"{id}\",\"{id}\"]}}"));
        var ids = string.Join(",", Enumerable.Range(0, 31).Select(_ => $"\"{Guid.NewGuid()}\""));
        Assert.Throws<ProfileValidationException>(() => Parse($"{{\"skill_ids\":[{ids}]}}"));
    }

    [Fact]
    public async Task Patch_UpdatesSpecifiedFieldsOnly_AndDoesNotTouchSkillsWhenOmitted()
    {
        var repo = new FakeRepository();
        var originalLinks = repo.User.Profile!.UserSkills.ToArray();
        var oldOnboarding = repo.User.IsOnboardingCompleted;
        var result = await new ProfileService(repo).UpdateMyProfileAsync(UserId,
            Parse("""{"bio":"  Updated bio  ","learning_goals":null}"""));

        Assert.Equal("Updated bio", repo.User.Profile.Bio);
        Assert.Null(repo.User.Profile.LearningGoals);
        Assert.Equal("FPT University", repo.User.Profile.University);
        Assert.Equal("Alex", repo.User.Profile.DisplayName);
        Assert.Equal(oldOnboarding, repo.User.IsOnboardingCompleted);
        Assert.Equal(originalLinks.Select(x => x.SkillId), repo.User.Profile.UserSkills.Select(x => x.SkillId));
        Assert.Equal(0, repo.SyncCalls);
        Assert.Equal(1, repo.SaveCalls);
        Assert.Equal("Updated bio", result.Bio);
    }

    [Fact]
    public async Task Patch_ReplacesSkills_AndKeepsPreviouslySelectedInactiveSkill()
    {
        var repo = new FakeRepository();
        var result = await new ProfileService(repo).UpdateMyProfileAsync(UserId,
            Parse($"{{\"skill_ids\":[\"{InactiveSkillId}\",\"{AddedSkillId}\"]}}"));

        Assert.Equal(1, repo.SyncCalls);
        Assert.DoesNotContain(repo.User.Profile!.UserSkills, link => link.SkillId == SkillId);
        Assert.Contains(repo.User.Profile.UserSkills, link => link.SkillId == InactiveSkillId);
        Assert.Contains(repo.User.Profile.UserSkills, link => link.SkillId == AddedSkillId);
        Assert.Contains(result.Skills, skill => skill.SkillId == AddedSkillId);
        Assert.Equal(1, repo.SaveCalls);
    }

    [Fact]
    public async Task Patch_RejectsInactiveNewSkill_AndRollsBackScalarChange()
    {
        var repo = new FakeRepository { AddedSkillsAreActive = false };
        await Assert.ThrowsAsync<BusinessRuleException>(() => new ProfileService(repo).UpdateMyProfileAsync(UserId,
            Parse($"{{\"headline\":\"new headline\",\"skill_ids\":[\"{SkillId}\",\"{AddedSkillId}\"]}}")));
        Assert.Null(repo.User.Profile!.Headline);
        Assert.Equal(0, repo.SaveCalls);
    }

    [Fact]
    public async Task Patch_RoleSameInactiveAllowed_ButChangedInactiveRejected()
    {
        var repo = new FakeRepository { CurrentRoleIsActive = false };
        await new ProfileService(repo).UpdateMyProfileAsync(UserId,
            Parse($"{{\"preferred_role_id\":\"{RoleId}\"}}"));
        Assert.Equal(0, repo.RoleChecks);
        await Assert.ThrowsAsync<BusinessRuleException>(() => new ProfileService(repo).UpdateMyProfileAsync(UserId,
            Parse($"{{\"preferred_role_id\":\"{Guid.NewGuid()}\"}}")));
        Assert.Equal(RoleId, repo.User.Profile!.PreferredRoleId);
    }

    [Fact]
    public async Task Patch_UnitOfWorkFailure_RollsBackScalarAndSkills()
    {
        var repo = new FakeRepository { ThrowDuringSave = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ProfileService(repo).UpdateMyProfileAsync(UserId,
            Parse($"{{\"headline\":\"Changed\",\"skill_ids\":[\"{AddedSkillId}\"]}}")));
        Assert.Null(repo.User.Profile!.Headline);
        Assert.Contains(repo.User.Profile.UserSkills, link => link.SkillId == SkillId);
        Assert.DoesNotContain(repo.User.Profile.UserSkills, link => link.SkillId == AddedSkillId);
    }

    [Fact]
    public async Task Patch_RejectsUnknownOrIncompleteAccount()
    {
        var repo = new FakeRepository();
        repo.User.IsOnboardingCompleted = false;
        await Assert.ThrowsAsync<NotFoundException>(() => new ProfileService(repo).UpdateMyProfileAsync(UserId, Parse("""{"bio":"test"}""")));
        Assert.Equal(0, repo.SaveCalls);
    }

    [Fact]
    public async Task Controller_Patch_ReturnsSuccessEnvelope_AndUsesDefaultPolicy()
    {
        var service = new ProfileService(new FakeRepository());
        var controller = new MyProfileController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId.ToString())], "Bearer"))
                }
            }
        };
        using var json = JsonDocument.Parse("""{"bio":"From controller"}""");
        var ok = Assert.IsType<OkObjectResult>(await controller.UpdateMyProfile(json.RootElement, CancellationToken.None));
        var serialized = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        using var parsed = JsonDocument.Parse(serialized);
        Assert.True(parsed.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("From controller", parsed.RootElement.GetProperty("data").GetProperty("bio").GetString());
        Assert.NotNull(typeof(MyProfileController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(typeof(MyProfileController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.NotNull(typeof(MyProfileController).GetMethod(nameof(MyProfileController.UpdateMyProfile))!.GetCustomAttribute<HttpPatchAttribute>());
    }

    [Fact]
    public async Task ValidationException_ProducesHttp400ProblemDetails()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/me/profile";
        context.Response.Body = new MemoryStream();
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var ex = Assert.Throws<ProfileValidationException>(() => Parse("""{"username":"bad"}"""));
        Assert.True(await handler.TryHandleAsync(context, ex, CancellationToken.None));
        Assert.Equal(400, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using var parsed = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("VALIDATION_FAILED", parsed.RootElement.GetProperty("error_code").GetString());
        Assert.True(parsed.RootElement.GetProperty("errors").TryGetProperty("username", out _));
    }

    private static UpdateProfileRequest Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ProfilePatchParser.Parse(document.RootElement);
    }

    private sealed class FakeRepository : IProfileRepository
    {
        public User User { get; private set; } = CreateUser();
        public bool AddedSkillsAreActive { get; set; } = true;
        public bool CurrentRoleIsActive { get; set; } = true;
        public bool ThrowDuringSave { get; set; }
        public int SaveCalls { get; private set; }
        public int SyncCalls { get; private set; }
        public int RoleChecks { get; private set; }

        public Task<User?> GetUserForUpdateAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(userId == UserId ? User : null);

        public Task<MyProfileResponse?> GetMyProfileAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            if (userId != UserId) return Task.FromResult<MyProfileResponse?>(null);
            var profile = User.Profile!;
            var response = new MyProfileResponse(UserId, User.Username, profile.DisplayName, profile.University,
                profile.PhoneNumber, profile.Headline, profile.Bio, profile.Location, profile.CurrentRole,
                profile.PreferredRoleId is null ? null : new PreferredRoleSummary(profile.PreferredRoleId.Value, "Backend"),
                profile.AvailableHoursPerWeek, profile.ProfessionalInterests, profile.LearningGoals,
                profile.PreviousProjectsAndContribution, profile.TechnologiesToLearn, profile.GithubUrl,
                profile.LinkedinUrl, profile.PersonalWebsiteUrl, profile.ResumeUrl, profile.PublicEmail,
                profile.AvatarUrl, profile.CoverImageUrl,
                profile.UserSkills.Select(x => new ProfileSkillSummary(x.SkillId, "Skill")).ToArray(), 0, 0);
            return Task.FromResult<MyProfileResponse?>(response);
        }

        public Task<bool> IsActiveProfessionalRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
        {
            RoleChecks++;
            return Task.FromResult(CurrentRoleIsActive);
        }

        public Task<IReadOnlyCollection<Guid>> GetActiveSkillIdsAsync(IReadOnlyCollection<Guid> skillIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<Guid>>(AddedSkillsAreActive ? skillIds.ToArray() : Array.Empty<Guid>());

        public void SyncUserSkills(UserProfile profile, IReadOnlyCollection<Guid> ids)
        {
            SyncCalls++;
            profile.UserSkills = ids.Select(id => new UserSkill { UserId = UserId, SkillId = id }).ToArray();
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            if (ThrowDuringSave) throw new InvalidOperationException("Simulated write failure.");
            return Task.CompletedTask;
        }

        public async Task<T> ExecuteInTransactionAsync<T>(Guid userId, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            var old = User.Profile!;
            var snapshot = new UserProfile
            {
                UserId = old.UserId, DisplayName = old.DisplayName, University = old.University, Headline = old.Headline,
                Bio = old.Bio, LearningGoals = old.LearningGoals, PreferredRoleId = old.PreferredRoleId,
                AvailableHoursPerWeek = old.AvailableHoursPerWeek,
                UserSkills = old.UserSkills.Select(x => new UserSkill { UserId = x.UserId, SkillId = x.SkillId }).ToArray()
            };
            try { return await operation(cancellationToken); }
            catch
            {
                User.Profile = snapshot;
                throw;
            }
        }

        private static User CreateUser()
        {
            var user = new User
            {
                UserId = UserId, Username = "alex", Status = "active", IsOnboardingCompleted = true,
                Profile = new UserProfile
                {
                    UserId = UserId, DisplayName = "Alex", University = "FPT University",
                    PreferredRoleId = RoleId, AvailableHoursPerWeek = 20, LearningGoals = "Old goal",
                    UserSkills = [
                        new UserSkill { UserId = UserId, SkillId = SkillId },
                        new UserSkill { UserId = UserId, SkillId = InactiveSkillId }
                    ]
                }
            };
            return user;
        }
    }
}
