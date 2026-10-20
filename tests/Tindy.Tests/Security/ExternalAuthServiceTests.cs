using Tindy.Application.Constants;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Models.Authentication;
using Tindy.Application.Services;
using Xunit;

namespace Tindy.Tests.Security;

/// <summary>Pure unit tests; do not require Docker, PostgreSQL or HTTP.</summary>
public sealed class ExternalAuthServiceTests
{
    private static AuthenticatedExternalIdentity Identity(
        string provider = "Google", string id = "google-123",
        string? email = "user@example.com", bool verified = true,
        string? name = "Sample User") =>
        new(provider, id, name, "sample", email, verified);

    [Fact]
    public async Task FirstGoogleSignIn_CreatesOAuthOnlyAggregate()
    {
        var repository = new InMemoryExternalAuthRepository();
        var service = new ExternalAuthService(repository);

        var result = await service.ResolveSignInAsync(Identity(email: " USER@Example.com "));

        var user = Assert.Single(repository.Users);
        Assert.Equal(user.UserId, result.UserId);
        Assert.True(result.IsNewUser);
        Assert.Null(user.Credential);
        Assert.Equal("USER@Example.com", user.PrimaryEmail);
        Assert.Equal("user@example.com", user.PrimaryEmailNormalized);
        Assert.NotNull(user.PrimaryEmailVerifiedAt);
        Assert.False(user.IsOnboardingCompleted);
        Assert.Equal("/onboarding", user.IsOnboardingCompleted ? "/discover" : "/onboarding");
        Assert.Equal(RoleNames.Member, Assert.Single(result.Roles));
        Assert.Equal("Sample User", user.Profile!.DisplayName);
        Assert.Null(user.Profile.Bio);
        Assert.Equal("Google", Assert.Single(user.ExternalAuthAccounts).Provider);
        Assert.Equal(1, repository.SaveCount);
    }

    [Theory]
    [InlineData("Google", "google-repeat")]
    [InlineData("GitHub", "github-repeat")]
    public async Task SecondSignIn_ResolvesSameUserIdWithoutDuplicate(string provider, string providerId)
    {
        var repository = new InMemoryExternalAuthRepository();
        var service = new ExternalAuthService(repository);
        var initial = await service.ResolveSignInAsync(Identity(provider, providerId));

        var repeated = await service.ResolveSignInAsync(Identity(provider, providerId, email: "changed@example.com"));

        Assert.Equal(initial.UserId, repeated.UserId);
        Assert.False(repeated.IsNewUser);
        Assert.Single(repository.Users);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task FirstGitHubSignIn_WithoutEmailOrName_IsAllowed()
    {
        var repository = new InMemoryExternalAuthRepository();
        var service = new ExternalAuthService(repository);
        var result = await service.ResolveSignInAsync(
            new AuthenticatedExternalIdentity("github", "9876", null, null, null, false));

        Assert.True(result.IsNewUser);
        Assert.Null(result.PrimaryEmail);
        Assert.Equal("Tindy Member", result.DisplayName);
        var user = Assert.Single(repository.Users);
        Assert.Null(user.PrimaryEmailNormalized);
        Assert.Null(user.Credential);
        Assert.NotNull(user.Profile);
        Assert.Single(user.UserRoles);
        Assert.Equal(RoleNames.Member, Assert.Single(result.Roles));
        Assert.Equal("GitHub", Assert.Single(repository.Users[0].ExternalAuthAccounts).Provider);
    }

    [Fact]
    public async Task UnverifiedProviderEmail_DoesNotBecomeVerifiedPrimaryEmail()
    {
        var repository = new InMemoryExternalAuthRepository();
        var service = new ExternalAuthService(repository);
        await service.ResolveSignInAsync(Identity(email: "notverified@example.com", verified: false));

        var user = Assert.Single(repository.Users);
        Assert.Null(user.PrimaryEmail);
        Assert.Null(user.PrimaryEmailNormalized);
        Assert.Null(user.PrimaryEmailVerifiedAt);
        Assert.Equal("notverified@example.com", Assert.Single(user.ExternalAuthAccounts).ProviderEmail);
    }

    [Fact]
    public async Task VerifiedEmailAlreadyBelongsToLocalAccount_RejectsAutoLink()
    {
        var repository = new InMemoryExternalAuthRepository();
        repository.ReservedEmails.Add("user@example.com");

        await Assert.ThrowsAsync<ConflictException>(
            () => new ExternalAuthService(repository).ResolveSignInAsync(Identity()));

        Assert.Empty(repository.Users);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task UnverifiedDuplicateEmail_DoesNotClaimIt()
    {
        var repository = new InMemoryExternalAuthRepository();
        repository.ReservedEmails.Add("user@example.com");
        await new ExternalAuthService(repository).ResolveSignInAsync(Identity(verified: false));
        Assert.Null(Assert.Single(repository.Users).PrimaryEmail);
    }

    [Theory]
    [InlineData("disabled", false)]
    [InlineData("suspended", false)]
    [InlineData("active", true)]
    public async Task ExistingSuspendedOrDeletedUser_CannotSignIn(string status, bool deleted)
    {
        var repository = new InMemoryExternalAuthRepository();
        var service = new ExternalAuthService(repository);
        await service.ResolveSignInAsync(Identity());
        repository.Users[0].Status = status;
        repository.Users[0].DeletedAt = deleted ? DateTime.UtcNow : null;

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => service.ResolveSignInAsync(Identity()));
    }

    [Fact]
    public async Task ExistingUserWithoutRole_IsRejected()
    {
        var repository = new InMemoryExternalAuthRepository();
        var service = new ExternalAuthService(repository);
        await service.ResolveSignInAsync(Identity());
        repository.Users[0].UserRoles.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ResolveSignInAsync(Identity()));
    }

    [Fact]
    public async Task ExistingUserOnboardingStatus_IsPreserved()
    {
        var repository = new InMemoryExternalAuthRepository();
        var service = new ExternalAuthService(repository);
        await service.ResolveSignInAsync(Identity());
        repository.Users[0].IsOnboardingCompleted = true;
        var result = await service.ResolveSignInAsync(Identity());
        Assert.True(result.IsOnboardingCompleted);
    }

    [Fact]
    public async Task LinkingSameIdentityTwice_IsIdempotent()
    {
        var repository = new InMemoryExternalAuthRepository();
        var service = new ExternalAuthService(repository);
        var initial = await service.ResolveSignInAsync(Identity());
        var linked = await service.LinkAsync(initial.UserId,
            Identity("GitHub", "github-456", "other@example.com"));
        var again = await service.LinkAsync(initial.UserId,
            Identity("GitHub", "github-456", "other@example.com"));

        Assert.True(linked);
        Assert.False(again);
        Assert.Equal(2, repository.SaveCount);
        Assert.Single(repository.Users);
        Assert.Equal(2, repository.Users[0].ExternalAuthAccounts.Count);
    }

    [Theory]
    [InlineData("Google", "google-local-link")]
    [InlineData("GitHub", "github-local-link")]
    public async Task LocalUser_CanExplicitlyLinkProvider_WithoutChangingPasswordOrEmail(
        string provider, string providerId)
    {
        var repository = new InMemoryExternalAuthRepository();
        var userId = Guid.NewGuid();
        var credential = new UserCredential
        {
            UserCredentialId = Guid.NewGuid(), UserId = userId,
            Email = "local@example.com", EmailNormalized = "local@example.com",
            PasswordHash = "existing-local-password-hash"
        };
        var local = new User
        {
            UserId = userId, Status = "active", PrimaryEmail = "local@example.com",
            PrimaryEmailNormalized = "local@example.com", Credential = credential,
            Profile = new UserProfile { UserId = userId, DisplayName = "Local User" },
            UserRoles = [new UserRole { UserId = userId, Role = repository.Role! }]
        };
        repository.Users.Add(local);

        // Explicit linking permits the same email; it never replaces the local login.
        var linked = await new ExternalAuthService(repository).LinkAsync(userId,
            Identity(provider, providerId, "local@example.com"));

        Assert.True(linked);
        Assert.Same(credential, local.Credential);
        Assert.Equal("local@example.com", local.PrimaryEmail);
        var account = Assert.Single(local.ExternalAuthAccounts);
        Assert.Equal(userId, account.UserId);
        Assert.Equal(provider, account.Provider);
        Assert.Equal(providerId, account.ProviderUserId);
        Assert.Single(repository.Users);
        Assert.Equal(1, repository.SaveCount);
    }

    [Theory]
    [InlineData("suspended", false)]
    [InlineData("active", true)]
    public async Task SuspendedOrDeletedUser_CannotLinkAnotherProvider(string status, bool deleted)
    {
        var repository = new InMemoryExternalAuthRepository();
        var service = new ExternalAuthService(repository);
        var original = await service.ResolveSignInAsync(Identity());
        var user = Assert.Single(repository.Users);
        user.Status = status;
        user.DeletedAt = deleted ? DateTime.UtcNow : null;

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => service.LinkAsync(original.UserId,
            Identity("GitHub", "new-identity", null, false)));

        Assert.Single(user.ExternalAuthAccounts);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task LinkingIdentityOwnedByAnotherUser_ReturnsConflict()
    {
        var repository = new InMemoryExternalAuthRepository();
        var service = new ExternalAuthService(repository);
        var a = await service.ResolveSignInAsync(Identity("Google", "a", "a@example.com"));
        var b = await service.ResolveSignInAsync(Identity("Google", "b", "b@example.com"));

        await Assert.ThrowsAsync<ConflictException>(
            () => service.LinkAsync(b.UserId, Identity("Google", "a", "a@example.com")));
        Assert.Equal(2, repository.SaveCount);
    }

    [Fact]
    public async Task LinkingSecondIdentityFromSameProvider_IsRejected()
    {
        var repository = new InMemoryExternalAuthRepository();
        var service = new ExternalAuthService(repository);
        var a = await service.ResolveSignInAsync(Identity());
        await Assert.ThrowsAsync<ConflictException>(
            () => service.LinkAsync(a.UserId, Identity("Google", "different", "other@example.com")));
    }

    [Fact]
    public async Task UnsupportedProvider_IsRejected()
    {
        var repository = new InMemoryExternalAuthRepository();
        await Assert.ThrowsAsync<ArgumentException>(
            () => new ExternalAuthService(repository).ResolveSignInAsync(Identity("Unknown")));
        Assert.Empty(repository.Users);
    }

    [Fact]
    public async Task NewUserWithoutSeededMemberRole_IsRejected()
    {
        var repository = new InMemoryExternalAuthRepository { Role = null };
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ExternalAuthService(repository).ResolveSignInAsync(Identity()));
    }

    [Theory]
    [InlineData("Google", "https://lh3.googleusercontent.com/a/sample")]
    [InlineData("GitHub", "https://avatars.githubusercontent.com/u/123?v=4")]
    public async Task NewOAuthUser_StoresProviderAvatar(string provider, string avatar)
    {
        var repo = new InMemoryExternalAuthRepository();
        await new ExternalAuthService(repo).ResolveSignInAsync(
            Identity(provider) with { AvatarUrl = avatar });
        Assert.Equal(avatar, Assert.Single(repo.Users).Profile!.AvatarUrl);
    }

    [Theory]
    [InlineData("Google", "javascript:alert(1)")]
    [InlineData("GitHub", "http://localhost/avatar.png")]
    [InlineData("Google", "")]
    public async Task InvalidOrMissingProviderAvatar_FallsBackToNull(string provider, string avatar)
    {
        var repo = new InMemoryExternalAuthRepository();
        await new ExternalAuthService(repo).ResolveSignInAsync(
            Identity(provider) with { AvatarUrl = avatar });
        Assert.Null(Assert.Single(repo.Users).Profile!.AvatarUrl);
    }

    [Theory]
    [InlineData("Google")]
    [InlineData("GitHub")]
    public async Task LinkingProvider_FillsOnlyEmptyAvatar(string provider)
    {
        var repo = new InMemoryExternalAuthRepository();
        var existingProvider = provider == "Google" ? "GitHub" : "Google";
        var user = await new ExternalAuthService(repo).ResolveSignInAsync(Identity(existingProvider));
        var profile = Assert.Single(repo.Users).Profile!;
        Assert.Null(profile.AvatarUrl);
        var service = new ExternalAuthService(repo);
        Assert.True(await service.LinkAsync(user.UserId, Identity(provider, provider + "-new",
            email: null, verified: false) with { AvatarUrl = "https://avatars.githubusercontent.com/u/12" }));
        Assert.Equal("https://avatars.githubusercontent.com/u/12", profile.AvatarUrl);
        Assert.Equal(2, repo.SaveCount);
    }

    [Fact]
    public async Task RepeatedLoginOrDifferentProviderLink_DoesNotOverwriteAvatar()
    {
        var repo = new InMemoryExternalAuthRepository();
        var service = new ExternalAuthService(repo);
        var original = await service.ResolveSignInAsync(
            Identity("Google", "g-original") with { AvatarUrl = "https://example.com/original.png" });
        await service.ResolveSignInAsync(Identity("Google", "g-original") with
        {
            AvatarUrl = "https://example.com/changed.png"
        });
        await service.LinkAsync(original.UserId, Identity("GitHub", "github-other") with
        {
            AvatarUrl = "https://example.com/github.png"
        });
        Assert.Equal("https://example.com/original.png", Assert.Single(repo.Users).Profile!.AvatarUrl);
    }

    [Fact]
    public async Task FailedCrossAccountLink_DoesNotSetAvatar()
    {
        var repo = new InMemoryExternalAuthRepository();
        var service = new ExternalAuthService(repo);
        var a = await service.ResolveSignInAsync(Identity("Google", "google-a") with
        {
            AvatarUrl = "https://example.com/a.png"
        });
        var b = await service.ResolveSignInAsync(Identity("GitHub", "github-b", email: "b@example.com")
            with { AvatarUrl = null });
        await Assert.ThrowsAsync<ConflictException>(() => service.LinkAsync(b.UserId,
            Identity("Google", "google-a") with { AvatarUrl = "https://example.com/swap.png" }));
        Assert.Null(repo.Users.Single(x => x.UserId == b.UserId).Profile!.AvatarUrl);
    }

    // An intentionally small fake: tests Application business rules only.
    private sealed class InMemoryExternalAuthRepository : IExternalAuthRepository
    {
        public List<User> Users { get; } = new();
        public HashSet<string> ReservedEmails { get; } = new(StringComparer.Ordinal);
        public Role? Role { get; set; } = new() { RoleId = Guid.NewGuid(), RoleName = RoleNames.Member };
        public int SaveCount { get; private set; }

        public Task<ExternalAuthAccount?> GetByProviderIdentityAsync(string provider, string providerUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.SelectMany(x => x.ExternalAuthAccounts)
                .FirstOrDefault(x => x.Provider == provider && x.ProviderUserId == providerUserId));

        public Task<ExternalAuthAccount?> GetByUserAndProviderAsync(Guid userId, string provider,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.SelectMany(x => x.ExternalAuthAccounts)
                .FirstOrDefault(x => x.UserId == userId && x.Provider == provider));

        public Task<User?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.FirstOrDefault(x => x.UserId == userId));

        public Task<bool> EmailReservedAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
            Task.FromResult(ReservedEmails.Contains(normalizedEmail) ||
                Users.Any(x => x.PrimaryEmailNormalized == normalizedEmail));

        public Task<Role?> GetRoleByNameAsync(string roleName, CancellationToken cancellationToken = default) =>
            Task.FromResult(Role is { RoleName: var name } && name == roleName ? Role : null);

        public Task AddUserAsync(User user, CancellationToken cancellationToken = default)
        {
            // Arrange loaded role navigation after materializing the test aggregate.
            var userRole = user.UserRoles.Single();
            userRole.Role = Role!;
            foreach (var account in user.ExternalAuthAccounts)
            {
                account.User = user;
            }
            Users.Add(user);
            return Task.CompletedTask;
        }

        public Task AddExternalAccountAsync(ExternalAuthAccount account, CancellationToken cancellationToken = default)
        {
            var user = Users.Single(x => x.UserId == account.UserId);
            account.User = user;
            user.ExternalAuthAccounts.Add(account);
            return Task.CompletedTask;
        }

        public async Task AddExternalAccountAndFillAvatarAsync(ExternalAuthAccount account,
            string? avatarUrl, CancellationToken cancellationToken = default)
        {
            await AddExternalAccountAsync(account, cancellationToken);
            var profile = Users.Single(x => x.UserId == account.UserId).Profile!;
            if (profile.AvatarUrl is null && avatarUrl is not null)
                profile.AvatarUrl = avatarUrl;
            await SaveChangesAsync(cancellationToken);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
