using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Tindy.Api.Authorization;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Models.Authorization;
using Xunit;

namespace Tindy.Tests.Security;

/// <summary>Fast authorization tests without Docker or PostgreSQL.</summary>
public sealed class ActiveAccountHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Theory]
    [InlineData(OnboardingAccessStatus.Incomplete)]
    [InlineData(OnboardingAccessStatus.Completed)]
    public async Task ActiveUser_IsAuthorized_RegardlessOfOnboarding(OnboardingAccessStatus status)
    {
        var reader = new FakeReader(status);
        var context = NewContext(ValidPrincipal());

        await new ActiveAccountHandler(reader).HandleAsync(context);

        Assert.True(context.HasSucceeded);
        Assert.Equal(UserId, reader.ReadFor);
    }

    [Fact]
    public async Task SuspendedOrDeletedUser_IsDenied_EvenWithValidJwtClaims()
    {
        var reader = new FakeReader(OnboardingAccessStatus.InactiveOrMissing);
        var context = NewContext(ValidPrincipal());

        await new ActiveAccountHandler(reader).HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "invalid-uuid")]
    [InlineData(true, "00000000-0000-0000-0000-000000000000")]
    public async Task UnauthenticatedOrMalformedSub_IsDeniedWithoutDatabaseCall(bool authenticated, string sub)
    {
        var claims = string.IsNullOrWhiteSpace(sub) ? Array.Empty<Claim>() : new[] { new Claim("sub", sub) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "Bearer" : null));
        var reader = new FakeReader(OnboardingAccessStatus.Completed);
        var context = NewContext(principal);

        await new ActiveAccountHandler(reader).HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.Null(reader.ReadFor);
    }

    private static AuthorizationHandlerContext NewContext(ClaimsPrincipal principal) =>
        new(new IAuthorizationRequirement[] { new ActiveAccountRequirement() },
            principal, new DefaultHttpContext());

    private static ClaimsPrincipal ValidPrincipal() =>
        new(new ClaimsIdentity(new[]
        {
            new Claim("sub", UserId.ToString("D")),
            new Claim("role", "member")
        }, "Bearer"));

    private sealed class FakeReader(OnboardingAccessStatus status) : IOnboardingAccessReader
    {
        public Guid? ReadFor { get; private set; }
        public Task<OnboardingAccessStatus> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            ReadFor = userId;
            return Task.FromResult(status);
        }
    }
}
