using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Authentication;
using Tindy.Application.Services;
using Xunit;

namespace Tindy.Tests.Security;

/// <summary>Fast unit tests: no Docker or actual OAuth provider required.</summary>
public sealed class OAuthLoginFlowServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static User MakeUser(bool onboarding = false, string status = "active") => new()
    {
        UserId = UserId, Status = status, IsOnboardingCompleted = onboarding,
        PrimaryEmail = "verified@example.com",
        Profile = new UserProfile { UserId = UserId, DisplayName = "Test OAuth User" },
        UserRoles = new List<UserRole>
        {
            new() { UserId = UserId, Role = new Role { RoleName = "member" } }
        }
    };

    private static OAuthLoginFlowService MakeService(FakeUsers users, FakeTickets tickets,
        FakeExternalAuth externalAuth, FakeJwt jwt) =>
        new(externalAuth, users, tickets, jwt);

    [Fact]
    public async Task SuccessfulExchange_IssuesTindyJwt_AndRedirectsToOnboarding()
    {
        var user = MakeUser();
        var users = new FakeUsers(user);
        var tickets = new FakeTickets(user.UserId);
        var jwt = new FakeJwt();
        var service = MakeService(users, tickets, new FakeExternalAuth(), jwt);
        var result = await service.ExchangeAsync("example-valid-ticket");

        Assert.Equal("test.tindy.jwt", result.AccessToken);
        Assert.Equal("Bearer", result.TokenType);
        Assert.Equal("/onboarding", result.RedirectTo);
        Assert.False(result.User.IsOnboardingCompleted);
        Assert.Equal(string.Empty, result.User.University);
        Assert.Equal(user.UserId, jwt.IssuedFor);
        Assert.Equal("member", Assert.Single(jwt.Roles!));
    }

    [Fact]
    public async Task CompletedOnboarding_UsesDiscover()
    {
        var user = MakeUser(onboarding: true);
        var result = await MakeService(new FakeUsers(user), new FakeTickets(user.UserId),
            new FakeExternalAuth(), new FakeJwt()).ExchangeAsync("valid");
        Assert.Equal("/discover", result.RedirectTo);
    }

    [Fact]
    public async Task OAuthOnlyUser_NoLocalCredentialAndNoEmail_CanExchange()
    {
        var user = MakeUser();
        user.PrimaryEmail = null;
        user.Credential = null;
        user.Profile!.University = "FPT University";

        var response = await MakeService(new FakeUsers(user), new FakeTickets(user.UserId),
            new FakeExternalAuth(), new FakeJwt()).ExchangeAsync("valid");

        Assert.Null(response.User.Email);
        Assert.Null(response.User.Username);
        Assert.Equal("FPT University", response.User.University);
        Assert.Equal("/onboarding", response.RedirectTo);
    }

    [Fact]
    public async Task MissingProfile_CannotIssueJwt()
    {
        var user = MakeUser();
        user.Profile = null;
        var jwt = new FakeJwt();
        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            MakeService(new FakeUsers(user), new FakeTickets(user.UserId),
                new FakeExternalAuth(), jwt).ExchangeAsync("valid"));
        Assert.Null(jwt.IssuedFor);
    }

    [Fact]
    public async Task ReplayedTicket_IsRejected()
    {
        var user = MakeUser();
        var service = MakeService(new FakeUsers(user), new FakeTickets(user.UserId),
            new FakeExternalAuth(), new FakeJwt());
        await service.ExchangeAsync("valid");
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => service.ExchangeAsync("valid"));
    }

    [Fact]
    public async Task InvalidOrExpiredTicket_DoesNotCreateJwt()
    {
        var jwt = new FakeJwt();
        var service = MakeService(new FakeUsers(MakeUser()), new FakeTickets(null),
            new FakeExternalAuth(), jwt);
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => service.ExchangeAsync("expired"));
        Assert.Null(jwt.IssuedFor);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public async Task BlankTicket_IsRejectedBeforeAccessingStore(string ticket)
    {
        var user = MakeUser();
        var tickets = new FakeTickets(user.UserId);
        var jwt = new FakeJwt();
        var service = MakeService(new FakeUsers(user), tickets, new FakeExternalAuth(), jwt);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => service.ExchangeAsync(ticket));

        Assert.Equal(0, tickets.ConsumeCount);
        Assert.Null(jwt.IssuedFor);
    }

    [Fact]
    public async Task TicketRejectedForInactiveUser_RemainsSpentAfterAccountIsReactivated()
    {
        var user = MakeUser(status: "suspended");
        var tickets = new FakeTickets(user.UserId);
        var jwt = new FakeJwt();
        var service = MakeService(new FakeUsers(user), tickets, new FakeExternalAuth(), jwt);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => service.ExchangeAsync("valid"));
        user.Status = "active";
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => service.ExchangeAsync("valid"));

        Assert.Equal(2, tickets.ConsumeCount);
        Assert.Null(jwt.IssuedFor);
    }

    [Theory]
    [InlineData("suspended", false)]
    [InlineData("active", true)]
    public async Task DisabledOrDeletedUser_CannotExchange(string status, bool deleted)
    {
        var user = MakeUser(status: status);
        if (deleted) user.DeletedAt = DateTime.UtcNow;
        var jwt = new FakeJwt();
        var service = MakeService(new FakeUsers(user), new FakeTickets(user.UserId),
            new FakeExternalAuth(), jwt);
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => service.ExchangeAsync("valid"));
        Assert.Null(jwt.IssuedFor);
    }

    [Fact]
    public async Task MissingRole_CannotExchange()
    {
        var user = MakeUser();
        user.UserRoles.Clear();
        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            MakeService(new FakeUsers(user), new FakeTickets(user.UserId),
                new FakeExternalAuth(), new FakeJwt()).ExchangeAsync("valid"));
    }

    [Fact]
    public async Task BeginTicket_UsesResolvedTindyIdentity_NotClientUserId()
    {
        var user = MakeUser();
        var auth = new FakeExternalAuth();
        var tickets = new FakeTickets(null);
        var service = MakeService(new FakeUsers(user), tickets, auth, new FakeJwt());
        var result = await service.BeginTicketAsync(new AuthenticatedExternalIdentity(
            "Google", "stable-google-id", "Test", null, "verified@example.com", true));
        Assert.Equal("new-ticket", result);
        Assert.Equal(UserId, tickets.IssuedFor);
        Assert.Equal("stable-google-id", auth.Identity?.ProviderUserId);
    }

    private sealed class FakeTickets(Guid? firstTicket) : IOAuthLoginTicketStore
    {
        private Guid? _remaining = firstTicket;
        public Guid? IssuedFor { get; private set; }
        public int ConsumeCount { get; private set; }
        public Task<string> IssueAsync(Guid userId, CancellationToken token = default)
        {
            IssuedFor = userId;
            return Task.FromResult("new-ticket");
        }
        public Task<Guid?> ConsumeAsync(string ticket, CancellationToken token = default)
        {
            ConsumeCount++;
            var current = _remaining;
            _remaining = null;
            return Task.FromResult(current);
        }
    }

    private sealed class FakeExternalAuth : IExternalAuthService
    {
        public AuthenticatedExternalIdentity? Identity { get; private set; }
        public Task<ExternalSignInResult> ResolveSignInAsync(
            AuthenticatedExternalIdentity identity, CancellationToken token = default)
        {
            Identity = identity;
            return Task.FromResult(new ExternalSignInResult(UserId, true,
                "Test", "verified@example.com", new[] { "member" }, false));
        }
        public Task<bool> LinkAsync(Guid userId, AuthenticatedExternalIdentity identity,
            CancellationToken token = default) => throw new NotImplementedException();
    }

    private sealed class FakeJwt : IJwtTokenService
    {
        public Guid? IssuedFor { get; private set; }
        public IReadOnlyCollection<string>? Roles { get; private set; }
        public AccessTokenResult GenerateAccessToken(Guid userId, IReadOnlyCollection<string> roles)
        {
            IssuedFor = userId;
            Roles = roles;
            return new AccessTokenResult("test.tindy.jwt", "Bearer", 3600);
        }
    }

    // Only GetUserByIdAsync is used by OAuthLoginFlowService.
    private sealed class FakeUsers(User user) : IExternalAuthRepository
    {
        public Task<User?> GetUserByIdAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<User?>(userId == user.UserId ? user : null);
        public Task<ExternalAuthAccount?> GetByProviderIdentityAsync(string provider,
            string providerUserId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExternalAuthAccount?> GetByUserAndProviderAsync(Guid userId,
            string provider, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> EmailReservedAsync(string email, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<Role?> GetRoleByNameAsync(string roleName, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task AddUserAsync(User newUser, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task AddExternalAccountAsync(ExternalAuthAccount account,
            CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveChangesAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}
