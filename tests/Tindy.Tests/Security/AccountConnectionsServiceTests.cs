using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Authentication;
using Tindy.Application.Services;
using Xunit;

namespace Tindy.Tests.Security;

/// <summary>STEP 05 business tests; no Docker/PostgreSQL dependency.</summary>
public sealed class AccountConnectionsServiceTests
{
    [Fact]
    public async Task Begin_RequiresActiveAccount_AndNormalizesProvider()
    {
        var setup = Setup();
        var start = await setup.Service.BeginAsync(setup.User, "github");
        Assert.Equal("start-token", start);
        Assert.Equal("GitHub", setup.Store.IssuedProvider);

        setup.Repository.Snapshot = null;
        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            setup.Service.BeginAsync(setup.User, "GitHub"));
    }

    [Fact]
    public async Task Challenge_IsSingleUse()
    {
        var setup = Setup();
        await setup.Service.BeginAsync(setup.User, "GitHub");
        var a = await setup.Service.BeginChallengeAsync("start-token", "github");
        var b = await setup.Service.BeginChallengeAsync("start-token", "github");
        Assert.NotNull(a);
        Assert.Null(b);
    }

    [Fact]
    public async Task Callback_OnlyCreatesCompletionTicket_DoesNotLink()
    {
        var setup = Setup();
        var identity = Identity();
        var ticket = await setup.Service.FinishProviderAsync(setup.Store.TransactionId, identity);
        Assert.Equal("completion-ticket", ticket);
        Assert.Equal(0, setup.External.LinkCount);
    }

    [Fact]
    public async Task LinkCompletion_PreservesAvatarForExplicitConfirmation()
    {
        var setup = Setup();
        var providerIdentity = Identity() with { AvatarUrl = "https://avatars.githubusercontent.com/u/9" };
        var ticket = await setup.Service.FinishProviderAsync(setup.Store.TransactionId, providerIdentity);
        Assert.Equal("completion-ticket", ticket);
        Assert.Equal(0, setup.External.LinkCount);

        await setup.Service.ConfirmAsync(setup.User, ticket!);
        Assert.Equal(providerIdentity.AvatarUrl, setup.External.LastIdentity!.AvatarUrl);
    }

    [Fact]
    public async Task Confirm_RequiresSameTindyUser_AndConsumesTicketOnce()
    {
        var setup = Setup();
        var wrongUser = Guid.NewGuid();
        var wrong = await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            setup.Service.ConfirmAsync(wrongUser, "completion-ticket"));
        Assert.NotNull(wrong);
        Assert.Equal(0, setup.External.LinkCount);

        var created = await setup.Service.ConfirmAsync(setup.User, "completion-ticket");
        Assert.True(created);
        Assert.Equal(setup.User, setup.External.LastLinkedUser);
        Assert.Equal("GitHub", setup.External.LastIdentity!.Provider);
        Assert.Equal(1, setup.External.LinkCount);
        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            setup.Service.ConfirmAsync(setup.User, "completion-ticket"));
    }

    [Fact]
    public async Task Confirm_DelegatesProviderOwnedByAnotherUserConflict()
    {
        var setup = Setup();
        setup.External.MustConflict = true;
        await Assert.ThrowsAsync<ConflictException>(() =>
            setup.Service.ConfirmAsync(setup.User, "completion-ticket"));
    }

    [Fact]
    public async Task Unlink_UsesNormalizedProvider_AndRepositoryInvariant()
    {
        var setup = Setup();
        await setup.Service.UnlinkAsync(setup.User, "google");
        Assert.Equal("Google", setup.Repository.LastUnlinkedProvider);
        setup.Repository.MustRejectLastMethod = true;
        await Assert.ThrowsAsync<ConflictException>(() =>
            setup.Service.UnlinkAsync(setup.User, "github"));
    }

    private static AuthenticatedExternalIdentity Identity() =>
        new("GitHub", "github-user-55", "MJoy", "mjoy", "mjoy@example.com", true);

    private static Fixture Setup()
    {
        var user = Guid.NewGuid();
        var repository = new FakeConnectionsRepository();
        var store = new FakeLinkStore(user);
        var external = new FakeExternalAuthService();
        return new Fixture(user, repository, store, external,
            new AccountConnectionsService(repository, store, external));
    }

    private sealed record Fixture(Guid User, FakeConnectionsRepository Repository,
        FakeLinkStore Store, FakeExternalAuthService External, AccountConnectionsService Service);

    private sealed class FakeConnectionsRepository : IAccountConnectionsRepository
    {
        public AccountConnectionsResult? Snapshot { get; set; } = new(false, Array.Empty<ConnectedProvider>());
        public bool MustRejectLastMethod { get; set; }
        public string? LastUnlinkedProvider { get; private set; }
        public Task<AccountConnectionsResult?> GetForActiveUserAsync(Guid userId,
            CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task UnlinkKeepingLastMethodAsync(Guid userId, string provider,
            CancellationToken cancellationToken = default)
        {
            LastUnlinkedProvider = provider;
            if (MustRejectLastMethod) throw new ConflictException("Last method.");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLinkStore : IOAuthLinkTransactionStore
    {
        private readonly Guid _userId;
        private bool _challenged;
        private bool _consumed;
        private AuthenticatedExternalIdentity? _completedIdentity;
        public Guid TransactionId { get; } = Guid.NewGuid();
        public string? IssuedProvider { get; private set; }
        public FakeLinkStore(Guid userId) => _userId = userId;
        public Task<string> IssueStartAsync(Guid userId, string provider,
            CancellationToken cancellationToken = default)
        {
            IssuedProvider = provider;
            return Task.FromResult("start-token");
        }
        public Task<Guid?> ConsumeStartAsync(string startToken, string provider,
            CancellationToken cancellationToken = default)
        {
            if (_challenged || startToken != "start-token" || provider != "GitHub")
                return Task.FromResult<Guid?>(null);
            _challenged = true;
            return Task.FromResult<Guid?>(TransactionId);
        }
        public Task<string?> CompleteProviderAsync(Guid transactionId,
            AuthenticatedExternalIdentity identity, CancellationToken cancellationToken = default)
        {
            if (transactionId != TransactionId) return Task.FromResult<string?>(null);
            _completedIdentity = identity;
            return Task.FromResult<string?>("completion-ticket");
        }
        public Task<AuthenticatedExternalIdentity?> ConsumeCompletionAsync(string linkTicket, Guid userId,
            CancellationToken cancellationToken = default)
        {
            if (_consumed || userId != _userId || linkTicket != "completion-ticket")
                return Task.FromResult<AuthenticatedExternalIdentity?>(null);
            _consumed = true;
            return Task.FromResult<AuthenticatedExternalIdentity?>(_completedIdentity ?? Identity());
        }
    }

    private sealed class FakeExternalAuthService : IExternalAuthService
    {
        public int LinkCount { get; private set; }
        public Guid LastLinkedUser { get; private set; }
        public AuthenticatedExternalIdentity? LastIdentity { get; private set; }
        public bool MustConflict { get; set; }
        public Task<ExternalSignInResult> ResolveSignInAsync(AuthenticatedExternalIdentity identity,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> LinkAsync(Guid currentUserId, AuthenticatedExternalIdentity identity,
            CancellationToken cancellationToken = default)
        {
            if (MustConflict) throw new ConflictException("Linked to another account.");
            LinkCount++;
            LastLinkedUser = currentUserId;
            LastIdentity = identity;
            return Task.FromResult(true);
        }
    }
}
