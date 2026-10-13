using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Tindy.Infrastructure.Authentication;
using Tindy.Infrastructure.Services;
using Xunit;

namespace Tindy.Tests.Security;

public sealed class CryptoTests
{
    [Fact]
    public void BCrypt_HashesWithRandomSalt_AndRejectsWrongPassword()
    {
        var hasher = new PasswordHasher();
        var a = hasher.HashPassword("Password123!");
        var b = hasher.HashPassword("Password123!");

        Assert.NotEqual(a, b);
        Assert.True(hasher.VerifyPassword("Password123!", a));
        Assert.True(hasher.VerifyPassword("Password123!", b));
        Assert.False(hasher.VerifyPassword("WrongPassword123!", a));
        Assert.False(hasher.VerifyPassword("Password123!", "malformed-hash"));
    }

    [Fact]
    public void BCrypt_RejectsPasswordsOver72Utf8Bytes()
    {
        var hasher = new PasswordHasher();
        Assert.Throws<ArgumentException>(() => hasher.HashPassword(new string('a', 73)));
    }

    [Fact]
    public void JWT_ValidatesSignatureIssuerAudienceAndRole()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        var options = new JwtOptions
        {
            Issuer = "Tindy.Api",
            Audience = "Tindy.Client",
            SigningKey = Convert.ToBase64String(bytes),
            AccessTokenLifetimeMinutes = 60,
            ClockSkewSeconds = 0
        };
        var service = new JwtTokenService(Options.Create(options));
        var userId = Guid.NewGuid();
        var result = service.GenerateAccessToken(userId, ["member", "member"]);

        Assert.Equal("Bearer", result.TokenType);
        Assert.Equal(3600, result.ExpiresIn);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var parsed = handler.ReadJwtToken(result.AccessToken);
        Assert.Single(parsed.Claims, c => c.Type == "role");

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(bytes),
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.Zero,
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
        var principal = handler.ValidateToken(result.AccessToken, parameters, out _);
        Assert.Equal(userId.ToString("D"), principal.FindFirst("sub")?.Value);
        Assert.True(principal.IsInRole("member"));

        parameters.ValidIssuer = "DifferentIssuer";
        Assert.Throws<SecurityTokenInvalidIssuerException>(() =>
            handler.ValidateToken(result.AccessToken, parameters, out _));
    }
}
