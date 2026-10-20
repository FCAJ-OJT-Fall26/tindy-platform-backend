using System.Security.Claims;
using Tindy.Api.Extensions;
using Tindy.Application.Models.Authentication;
using Xunit;

namespace Tindy.Tests.Security;

public sealed class OAuthAvatarReaderTests
{
    [Theory]
    [InlineData("Google", "tindy:google_avatar_url", "https://lh3.googleusercontent.com/a/photo")]
    [InlineData("GitHub", "tindy:github_avatar_url", "https://avatars.githubusercontent.com/u/42?v=4")]
    public void Reader_MapsOnlyProviderAvatarClaim(string provider, string avatarClaim, string avatar)
    {
        var principal = Principal(new Claim(avatarClaim, avatar));
        var identity = AuthenticatedExternalIdentityReader.Read(principal, provider);
        Assert.NotNull(identity);
        Assert.Equal(avatar, identity.AvatarUrl);
    }

    [Theory]
    [InlineData("http://example.com/avatar.png")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:password@example.com/avatar")]
    [InlineData("https://example.com/avatar#fragment")]
    [InlineData("")]
    public void Reader_RejectsInvalidAvatar(string avatar)
    {
        Assert.Null(AuthenticatedExternalIdentityReader.Read(
            Principal(new Claim("tindy:google_avatar_url", avatar)), "Google")!.AvatarUrl);
    }

    [Fact]
    public void Reader_MissingAvatar_AndWrongProviderClaim_DefaultsToNull()
    {
        Assert.Null(AuthenticatedExternalIdentityReader.Read(Principal(), "Google")!.AvatarUrl);
        Assert.Null(AuthenticatedExternalIdentityReader.Read(
            Principal(new Claim("tindy:google_avatar_url", "https://example.com/x")), "GitHub")!.AvatarUrl);
    }

    [Fact]
    public void Reader_DoesNotAcceptClientIdentityWithoutProviderSubject()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tindy:google_avatar_url", "https://example.com/a")], "provider"));
        Assert.Null(AuthenticatedExternalIdentityReader.Read(principal, "Google"));
    }

    [Fact]
    public void AvatarValidator_AcceptsHttps_AndRejectsTooLongUrl()
    {
        Assert.Equal("https://example.com/p.png", OAuthAvatarUrl.Normalize(" https://example.com/p.png "));
        Assert.Null(OAuthAvatarUrl.Normalize("https://example.com/" + new string('x', 1001)));
    }

    private static ClaimsPrincipal Principal(params Claim[] extra)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "provider-123"),
            new Claim(ClaimTypes.Name, "Profile User")
        }.Concat(extra);
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "provider"));
    }
}
