using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Application.Constants;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Authentication;
using Tindy.Application.Services;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Repositories;
using Xunit;

namespace Tindy.Tests.Integration;

/// <summary>
/// Real PostgreSQL conditional writes and serializable transactions; every concurrent
/// request has its own EF scope. Uses only the private Testcontainers database.
/// </summary>
public sealed class OAuthStoreSecurityTests : IClassFixture<TindyApiFactory>
{
    private readonly TindyApiFactory _factory;

    public OAuthStoreSecurityTests(TindyApiFactory factory) => _factory = factory;

    [Fact]
    public async Task LoginTicket_IsHashed_HasNinetySecondLifetime_AndCannotReplay()
    {
        var userId = await CreateUserAsync();
        var ticket = await WithScopeAsync(services => services
            .GetRequiredService<IOAuthLoginTicketStore>().IssueAsync(userId));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            var row = await db.OAuthLoginTickets.AsNoTracking().SingleAsync(x => x.UserId == userId);
            Assert.Equal(43, ticket.Length);
            var raw = Convert.FromBase64String(ticket.Replace('-', '+').Replace('_', '/') + "=");
            Assert.Equal(Convert.ToHexString(SHA256.HashData(raw)), row.TicketHash);
            Assert.Equal(TimeSpan.FromSeconds(90), row.ExpiresAt - row.CreatedAt);
        }

        Assert.Equal(userId, await ConsumeLoginAsync(ticket));
        Assert.Null(await ConsumeLoginAsync(ticket));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task MalformedOrUnknownLoginTicket_IsRejected(string ticket) =>
        Assert.Null(await ConsumeLoginAsync(ticket));

    [Fact]
    public async Task ExpiredLoginTicket_IsRejected()
    {
        var userId = await CreateUserAsync();
        var ticket = await WithScopeAsync(services => services
            .GetRequiredService<IOAuthLoginTicketStore>().IssueAsync(userId));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            await db.OAuthLoginTickets.Where(x => x.UserId == userId)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.ExpiresAt,
                    DateTime.UtcNow.AddSeconds(-1)));
        }

        Assert.Null(await ConsumeLoginAsync(ticket));
    }

    [Fact]
    public async Task ConcurrentLoginExchange_OnlyOneRequestIssuesJwt()
    {
        var userId = await CreateUserAsync();
        var ticket = await WithScopeAsync(services => services
            .GetRequiredService<IOAuthLoginTicketStore>().IssueAsync(userId));

        // Both requests must read the same candidate before either can delete it.
        var barrier = new ConcurrentReadBarrier("oauth_login_tickets");
        var outcomes = await Task.WhenAll(AttemptWithDatabaseAsync(barrier, async (db, services) =>
            {
                var response = await CreateLoginFlow(db, services).ExchangeAsync(ticket);
                Assert.Equal(userId, response.User.UserId);
                Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));
            }), AttemptWithDatabaseAsync(barrier, async (db, services) =>
            {
                var response = await CreateLoginFlow(db, services).ExchangeAsync(ticket);
                Assert.Equal(userId, response.User.UserId);
                Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));
            }));

        Assert.Single(outcomes, error => error is null);
        Assert.IsType<InvalidCredentialsException>(Assert.Single(outcomes, error => error is not null));
        Assert.Null(await ConsumeLoginAsync(ticket));
    }

    [Fact]
    public async Task LinkStart_IsProviderBound_AndCanOnlyBeConsumedOnce()
    {
        var userId = await CreateUserAsync();
        var start = await IssueLinkStartAsync(userId);

        Assert.Null(await WithScopeAsync(services => services
            .GetRequiredService<IOAuthLinkTransactionStore>().ConsumeStartAsync(start, "Google")));
        var transaction = await ConsumeLinkStartAsync(start);
        Assert.NotNull(transaction);
        Assert.Null(await ConsumeLinkStartAsync(start));

        Assert.Null(await WithScopeAsync(services => services
            .GetRequiredService<IOAuthLinkTransactionStore>().CompleteProviderAsync(
                transaction!.Value, Identity("Google"))));
        Assert.NotNull(await WithScopeAsync(services => services
            .GetRequiredService<IOAuthLinkTransactionStore>().CompleteProviderAsync(
                transaction!.Value, Identity())));
    }

    [Fact]
    public async Task ExpiredLinkStart_CannotBeginChallenge()
    {
        var userId = await CreateUserAsync();
        var start = await IssueLinkStartAsync(userId);
        await ExpireLinkTransactionAsync(userId);

        Assert.Null(await ConsumeLinkStartAsync(start));
    }

    [Fact]
    public async Task ConcurrentLinkStart_OnlyOneChallengeCanBegin()
    {
        var userId = await CreateUserAsync();
        var start = await IssueLinkStartAsync(userId);

        var outcomes = await Task.WhenAll(ConsumeLinkStartAsync(start), ConsumeLinkStartAsync(start));

        Assert.Single(outcomes, transaction => transaction.HasValue);
        Assert.Single(outcomes, transaction => !transaction.HasValue);
    }

    [Fact]
    public async Task LinkCompletion_RequiresOriginalUser_AndCannotReplay()
    {
        var userId = await CreateUserAsync();
        var ticket = await IssueLinkCompletionAsync(userId);

        Assert.Null(await ConsumeLinkCompletionAsync(ticket, Guid.NewGuid()));
        var identity = await ConsumeLinkCompletionAsync(ticket, userId);
        Assert.NotNull(identity);
        Assert.Equal("GitHub", identity.Provider);
        Assert.Null(identity.Email);
        Assert.Null(await ConsumeLinkCompletionAsync(ticket, userId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-link-ticket")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task InvalidLinkCompletion_IsRejected(string ticket) =>
        Assert.Null(await ConsumeLinkCompletionAsync(ticket, Guid.NewGuid()));

    [Fact]
    public async Task ExpiredLinkCompletion_CannotBeConfirmed()
    {
        var userId = await CreateUserAsync();
        var ticket = await IssueLinkCompletionAsync(userId);
        await ExpireLinkTransactionAsync(userId);

        Assert.Null(await ConsumeLinkCompletionAsync(ticket, userId));
    }

    [Fact]
    public async Task ConcurrentLinkCompletion_OnlyOneRequestReceivesIdentity()
    {
        var userId = await CreateUserAsync();
        var ticket = await IssueLinkCompletionAsync(userId);

        var barrier = new ConcurrentReadBarrier("oauth_link_transactions");
        var outcomes = await Task.WhenAll(ConsumeLinkCompletionWithBarrierAsync(ticket, userId, barrier),
            ConsumeLinkCompletionWithBarrierAsync(ticket, userId, barrier));

        Assert.Single(outcomes, identity => identity is not null);
        Assert.Single(outcomes, identity => identity is null);
    }

    [Fact]
    public async Task Unlink_FinalOAuthMethodIsRejected_AndConnectionRemains()
    {
        var userId = await CreateUserAsync(providers: ["Google"]);

        var error = await AttemptAsync(services => services
            .GetRequiredService<IAccountConnectionsRepository>()
            .UnlinkKeepingLastMethodAsync(userId, "Google"));

        Assert.IsType<ConflictException>(error);
        Assert.Equal(1, await CountProvidersAsync(userId));
    }

    [Fact]
    public async Task Unlink_WithLocalPassword_CanRemoveOnlyOAuthProvider()
    {
        var userId = await CreateUserAsync(hasLocalPassword: true, providers: ["Google"]);

        var error = await AttemptAsync(services => services
            .GetRequiredService<IAccountConnectionsRepository>()
            .UnlinkKeepingLastMethodAsync(userId, "Google"));

        Assert.Null(error);
        Assert.Equal(0, await CountProvidersAsync(userId));
        var snapshot = await WithScopeAsync(services => services
            .GetRequiredService<IAccountConnectionsRepository>().GetForActiveUserAsync(userId));
        Assert.True(snapshot!.HasLocalPassword);
    }

    [Fact]
    public async Task ConcurrentUnlink_LeavesAtLeastOneSignInMethod()
    {
        var userId = await CreateUserAsync(providers: ["Google", "GitHub"]);

        // Force both serializable snapshots to observe two providers before deleting.
        var barrier = new ConcurrentReadBarrier("external_auth_accounts", countOnly: true);
        var outcomes = await Task.WhenAll(AttemptWithDatabaseAsync(barrier, (db, _) =>
                new AccountConnectionsRepository(db).UnlinkKeepingLastMethodAsync(userId, "Google")),
            AttemptWithDatabaseAsync(barrier, (db, _) =>
                new AccountConnectionsRepository(db).UnlinkKeepingLastMethodAsync(userId, "GitHub")));

        Assert.Single(outcomes, error => error is null);
        Assert.IsType<ConflictException>(Assert.Single(outcomes, error => error is not null));
        Assert.Equal(1, await CountProvidersAsync(userId));
    }

    private Task<Guid?> ConsumeLoginAsync(string ticket) => WithScopeAsync(services => services
        .GetRequiredService<IOAuthLoginTicketStore>().ConsumeAsync(ticket));

    private Task<string> IssueLinkStartAsync(Guid userId) => WithScopeAsync(services => services
        .GetRequiredService<IOAuthLinkTransactionStore>().IssueStartAsync(userId, "GitHub"));

    private Task<Guid?> ConsumeLinkStartAsync(string start) => WithScopeAsync(services => services
        .GetRequiredService<IOAuthLinkTransactionStore>().ConsumeStartAsync(start, "GitHub"));

    private Task<AuthenticatedExternalIdentity?> ConsumeLinkCompletionAsync(string ticket, Guid userId) =>
        WithScopeAsync(services => services.GetRequiredService<IOAuthLinkTransactionStore>()
            .ConsumeCompletionAsync(ticket, userId));

    private async Task<string> IssueLinkCompletionAsync(Guid userId)
    {
        var start = await IssueLinkStartAsync(userId);
        var transaction = await ConsumeLinkStartAsync(start);
        Assert.NotNull(transaction);
        var ticket = await WithScopeAsync(services => services
            .GetRequiredService<IOAuthLinkTransactionStore>()
            .CompleteProviderAsync(transaction.Value, Identity()));
        Assert.NotNull(ticket);
        return ticket;
    }

    private async Task ExpireLinkTransactionAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        await db.OAuthLinkTransactions.Where(x => x.UserId == userId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ExpiresAt,
                DateTime.UtcNow.AddSeconds(-1)));
    }

    private Task<int> CountProvidersAsync(Guid userId) => WithScopeAsync(services => services
        .GetRequiredService<TindyDbContext>().ExternalAuthAccounts.CountAsync(x => x.UserId == userId));

    private async Task<Guid> CreateUserAsync(bool hasLocalPassword = false, params string[] providers)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var roleId = await db.Roles.Where(x => x.RoleName == RoleNames.Member)
            .Select(x => x.RoleId).SingleAsync();
        var userId = Guid.NewGuid();
        var email = $"store.{userId:N}@example.com";
        var user = new User
        {
            UserId = userId,
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            Profile = new UserProfile { UserId = userId, DisplayName = "Store Test", CreatedAt = DateTime.UtcNow },
            UserRoles = [new UserRole { UserId = userId, RoleId = roleId }],
            ExternalAuthAccounts = providers.Select(provider => new ExternalAuthAccount
            {
                ExternalAuthAccountId = Guid.NewGuid(), UserId = userId,
                Provider = provider, ProviderUserId = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.UtcNow
            }).ToList()
        };
        if (hasLocalPassword)
        {
            user.Credential = new UserCredential
            {
                UserCredentialId = Guid.NewGuid(), UserId = userId, Email = email,
                EmailNormalized = email, PasswordHash = "unused-test-fixture-hash",
                CreatedAt = DateTime.UtcNow
            };
        }
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return userId;
    }

    private async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    private async Task<Exception?> AttemptAsync(Func<IServiceProvider, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await Record.ExceptionAsync(() => action(scope.ServiceProvider));
    }

    private async Task<Exception?> AttemptWithDatabaseAsync(DbCommandInterceptor interceptor,
        Func<TindyDbContext, IServiceProvider, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        await using var db = CreateConcurrentContext(scope.ServiceProvider, interceptor);
        return await Record.ExceptionAsync(() => action(db, scope.ServiceProvider));
    }

    private async Task<AuthenticatedExternalIdentity?> ConsumeLinkCompletionWithBarrierAsync(
        string ticket, Guid userId, DbCommandInterceptor interceptor)
    {
        using var scope = _factory.Services.CreateScope();
        await using var db = CreateConcurrentContext(scope.ServiceProvider, interceptor);
        return await new OAuthLinkTransactionStore(db).ConsumeCompletionAsync(ticket, userId);
    }

    private static TindyDbContext CreateConcurrentContext(IServiceProvider services,
        DbCommandInterceptor interceptor)
    {
        // Copy only the fixture's throwaway container connection, never app settings.
        var connection = services.GetRequiredService<TindyDbContext>().Database.GetConnectionString();
        var options = new DbContextOptionsBuilder<TindyDbContext>().UseNpgsql(connection)
            .AddInterceptors(interceptor).Options;
        return new TindyDbContext(options);
    }

    private static OAuthLoginFlowService CreateLoginFlow(TindyDbContext db, IServiceProvider services)
    {
        var users = new ExternalAuthRepository(db);
        return new OAuthLoginFlowService(new ExternalAuthService(users), users,
            new OAuthLoginTicketStore(db), services.GetRequiredService<IJwtTokenService>());
    }

    private sealed class ConcurrentReadBarrier(string table, bool countOnly = false) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothReaders = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(table, StringComparison.Ordinal) &&
                (!countOnly || command.CommandText.Contains("count(", StringComparison.OrdinalIgnoreCase)))
            {
                if (Interlocked.Increment(ref _arrivals) == 2) _bothReaders.TrySetResult();
                await _bothReaders.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }

    private static AuthenticatedExternalIdentity Identity(string provider = "GitHub") =>
        new(provider, Guid.NewGuid().ToString("N"), "Private Email User", "private-user", null, false);
}
