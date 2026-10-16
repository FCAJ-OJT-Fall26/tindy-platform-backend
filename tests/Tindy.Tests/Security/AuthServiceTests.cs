using Tindy.Application.Constants;
using Tindy.Application.DTOs.Authentication;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Authentication;
using Tindy.Application.Services;
using Tindy.Infrastructure.Services;
using Xunit;

namespace Tindy.Tests.Security;

/// <summary>Actual local auth business logic and BCrypt; no database or provider calls.</summary>
public sealed class AuthServiceTests
{
    private const string TestPassword = "Password123!";
    private static readonly PasswordHasher Hasher = new();
    private static readonly Lazy<string> ExistingHash = new(() => Hasher.HashPassword(TestPassword));

    [Fact]
    public async Task Register_CreatesLocalAggregateWithMemberRole_AndStartsOnboarding()
    {
        var repository = new MemoryAuthRepository();
        var jwt = new RecordingJwt();
        var service = new AuthService(repository, Hasher, jwt);

        var response = await service.RegisterAsync(new RegisterRequest
        {
            DisplayName = " Local User ", University = " FPT University ",
            Email = " LOCAL@Example.com ", Password = TestPassword, AcceptTerms = true
        });

        var user = Assert.Single(repository.Users);
        Assert.NotEqual(Guid.Empty, user.UserId);
        Assert.Equal(user.UserId, response.User.UserId);
        Assert.Equal("active", user.Status);
        Assert.Null(user.Username);
        Assert.Equal("LOCAL@Example.com", user.PrimaryEmail);
        Assert.Equal("local@example.com", user.PrimaryEmailNormalized);
        Assert.Equal("local@example.com", repository.LastEmailExistenceCheck);
        Assert.Null(user.PrimaryEmailVerifiedAt);
        Assert.NotNull(user.Credential);
        Assert.Equal(user.UserId, user.Credential.UserId);
        Assert.Equal("local@example.com", user.Credential.EmailNormalized);
        Assert.True(Hasher.VerifyPassword(TestPassword, user.Credential.PasswordHash));
        Assert.Null(user.Credential.EmailVerifiedAt);
        Assert.NotNull(user.Profile);
        Assert.Equal(user.UserId, user.Profile.UserId);
        Assert.Equal("Local User", user.Profile.DisplayName);
        Assert.Equal("FPT University", user.Profile.University);
        Assert.Null(user.Profile.Bio);
        var membership = Assert.Single(user.UserRoles);
        Assert.Equal(repository.MemberRole.RoleId, membership.RoleId);
        Assert.Empty(user.ExternalAuthAccounts);
        Assert.False(user.IsOnboardingCompleted);
        Assert.False(response.User.IsOnboardingCompleted);
        Assert.Equal("/onboarding", response.RedirectTo);
        Assert.Equal(RoleNames.Member, Assert.Single(response.User.Roles));
        Assert.Equal(user.UserId, jwt.LastUserId);
        Assert.Equal(1, jwt.IssueCount);
        Assert.Equal(1, repository.SaveCount);
    }

    [Theory]
    [InlineData(" LOCAL@EXAMPLE.COM ", false, "/onboarding")]
    [InlineData(" LOCAL@EXAMPLE.COM ", true, "/discover")]
    [InlineData(" LOCAL-USER ", false, "/onboarding")]
    [InlineData(" LOCAL-USER ", true, "/discover")]
    public async Task Login_NormalizesEmailOrUsername_AndUsesCurrentOnboardingState(
        string identifier, bool completed, string redirect)
    {
        var user = MakeLocalUser();
        user.IsOnboardingCompleted = completed;
        var repository = new MemoryAuthRepository();
        repository.Users.Add(user);
        var jwt = new RecordingJwt();

        var response = await new AuthService(repository, Hasher, jwt)
            .LoginAsync(new LoginRequest { Identifier = identifier, Password = TestPassword });

        Assert.Equal(user.UserId, response.User.UserId);
        Assert.Equal(user.UserId, jwt.LastUserId);
        Assert.Equal(1, jwt.IssueCount);
        Assert.Equal(completed, response.User.IsOnboardingCompleted);
        Assert.Equal(redirect, response.RedirectTo);
        Assert.Equal(RoleNames.Member, Assert.Single(response.User.Roles));
        if (identifier.Contains('@'))
        {
            Assert.Equal("local@example.com", repository.LastEmailLookup);
            Assert.Null(repository.LastUsernameLookup);
        }
        else
        {
            Assert.Equal("local-user", repository.LastUsernameLookup);
            Assert.Null(repository.LastEmailLookup);
        }
    }

    [Theory]
    [InlineData("suspended", false)]
    [InlineData("active", true)]
    public async Task Login_SuspendedOrDeletedAccount_CannotIssueJwt(string status, bool deleted)
    {
        var user = MakeLocalUser();
        user.Status = status;
        user.DeletedAt = deleted ? DateTime.UtcNow : null;
        var repository = new MemoryAuthRepository();
        repository.Users.Add(user);
        var jwt = new RecordingJwt();

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => new AuthService(repository, Hasher, jwt)
            .LoginAsync(new LoginRequest { Identifier = "local@example.com", Password = TestPassword }));

        Assert.Equal(0, jwt.IssueCount);
    }

    [Fact]
    public async Task Login_OAuthOnlyAccountWithUsername_CannotUseLocalPassword()
    {
        var user = MakeLocalUser();
        user.Credential = null;
        user.ExternalAuthAccounts.Add(new ExternalAuthAccount
        {
            ExternalAuthAccountId = Guid.NewGuid(), UserId = user.UserId,
            Provider = "GitHub", ProviderUserId = "oauth-only-test-identity"
        });
        var repository = new MemoryAuthRepository();
        repository.Users.Add(user);
        var jwt = new RecordingJwt();

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => new AuthService(repository, Hasher, jwt)
            .LoginAsync(new LoginRequest { Identifier = "LOCAL-USER", Password = TestPassword }));

        Assert.Equal(0, jwt.IssueCount);
        Assert.Null(user.Credential);
    }

    [Fact]
    public async Task Login_IncorrectPassword_CannotIssueJwt()
    {
        var repository = new MemoryAuthRepository();
        repository.Users.Add(MakeLocalUser());
        var jwt = new RecordingJwt();

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => new AuthService(repository, Hasher, jwt)
            .LoginAsync(new LoginRequest { Identifier = "local@example.com", Password = "WrongPassword123!" }));

        Assert.Equal(0, jwt.IssueCount);
    }

    [Fact]
    public async Task Register_DuplicateNormalizedEmail_DoesNotCreateAnotherUserOrJwt()
    {
        var repository = new MemoryAuthRepository();
        repository.Users.Add(MakeLocalUser());
        var jwt = new RecordingJwt();

        await Assert.ThrowsAsync<EmailAlreadyExistsException>(() => new AuthService(repository, Hasher, jwt)
            .RegisterAsync(new RegisterRequest
            {
                DisplayName = "Duplicate", University = "FPT University", Email = " LOCAL@EXAMPLE.COM ",
                Password = TestPassword, AcceptTerms = true
            }));

        Assert.Single(repository.Users);
        Assert.Equal(0, repository.SaveCount);
        Assert.Equal(0, jwt.IssueCount);
    }

    private static User MakeLocalUser()
    {
        var userId = Guid.NewGuid();
        return new User
        {
            UserId = userId, Username = "local-user", UsernameNormalized = "local-user",
            PrimaryEmail = "local@example.com", PrimaryEmailNormalized = "local@example.com", Status = "active",
            Profile = new UserProfile { UserId = userId, DisplayName = "Local User", University = "FPT University" },
            Credential = new UserCredential
            {
                UserCredentialId = Guid.NewGuid(), UserId = userId,
                Email = "local@example.com", EmailNormalized = "local@example.com", PasswordHash = ExistingHash.Value
            },
            UserRoles = [new UserRole { UserId = userId, Role = new Role { RoleName = RoleNames.Member } }]
        };
    }

    private sealed class RecordingJwt : IJwtTokenService
    {
        public Guid? LastUserId { get; private set; }
        public int IssueCount { get; private set; }
        public AccessTokenResult GenerateAccessToken(Guid userId, IReadOnlyCollection<string> roles)
        {
            LastUserId = userId;
            IssueCount++;
            return new AccessTokenResult("local-unit-test-token", "Bearer", 3600);
        }
    }

    private sealed class MemoryAuthRepository : IAuthRepository
    {
        public List<User> Users { get; } = [];
        public Role MemberRole { get; } = new() { RoleId = Guid.NewGuid(), RoleName = RoleNames.Member };
        public string? LastEmailExistenceCheck { get; private set; }
        public string? LastEmailLookup { get; private set; }
        public string? LastUsernameLookup { get; private set; }
        public int SaveCount { get; private set; }

        public Task<bool> EmailExistsAsync(string emailNormalized, CancellationToken cancellationToken = default)
        {
            LastEmailExistenceCheck = emailNormalized;
            return Task.FromResult(Users.Any(user => user.PrimaryEmailNormalized == emailNormalized ||
                user.Credential?.EmailNormalized == emailNormalized));
        }
        public Task<bool> UsernameExistsAsync(string usernameNormalized, CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.Any(user => user.UsernameNormalized == usernameNormalized));
        public Task<User?> GetByEmailAsync(string emailNormalized, CancellationToken cancellationToken = default)
        {
            LastEmailLookup = emailNormalized;
            return Task.FromResult(Users.FirstOrDefault(user => user.Credential?.EmailNormalized == emailNormalized));
        }
        public Task<User?> GetByUsernameAsync(string usernameNormalized, CancellationToken cancellationToken = default)
        {
            LastUsernameLookup = usernameNormalized;
            return Task.FromResult(Users.FirstOrDefault(user => user.UsernameNormalized == usernameNormalized));
        }
        public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.FirstOrDefault(user => user.UserId == userId));
        public Task<Role?> GetRoleByNameAsync(string roleName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Role?>(MemberRole.RoleName == roleName ? MemberRole : null);
        public Task AddUserAsync(User user, CancellationToken cancellationToken = default)
        {
            Users.Add(user);
            return Task.CompletedTask;
        }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }
}
