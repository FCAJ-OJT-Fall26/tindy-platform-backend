
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Models.Authentication;
using Tindy.Infrastructure.Authentication;

namespace Tindy.Infrastructure.Services;

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _jwtOptions;
    private readonly SigningCredentials _signingCredentials;

    // ===================================
    // 1. CONSTRUCTOR
    // ===================================
    public JwtTokenService(IOptions<JwtOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _jwtOptions = options.Value;

        // Validate JWT configuration
        if (string.IsNullOrWhiteSpace(_jwtOptions.Issuer) ||
            string.IsNullOrWhiteSpace(_jwtOptions.Audience))
        {
            throw new InvalidOperationException("JWT Issuer and Audience are required.");
        }

        if (_jwtOptions.AccessTokenLifetimeMinutes <= 0 ||
            _jwtOptions.AccessTokenLifetimeMinutes > int.MaxValue / 60)
        {
            throw new InvalidOperationException("JWT Access Token lifetime is invalid.");
        }

        if (string.IsNullOrWhiteSpace(_jwtOptions.SigningKey))
        {
            throw new InvalidOperationException("JWT SigningKey was not configured.");
        }

        byte[] keyBytes;

        try
        {
            keyBytes = Convert.FromBase64String(_jwtOptions.SigningKey);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("JWT SigningKey must be valid Base64.", ex);
        }

        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException("JWT SigningKey must contain at least 32 bytes.");
        }

        var securityKey = new SymmetricSecurityKey(keyBytes);

        _signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
    }

    // ===================================
    // 2. GENERATE ACCESS TOKEN
    // ===================================
    public AccessTokenResult GenerateAccessToken(Guid userId, IReadOnlyCollection<string> roles)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(roles);

        var roleNames = roles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (roleNames.Length == 0)
        {
            throw new ArgumentException("At least one role is required.", nameof(roles));
        }

        // ===================================
        // 3. TOKEN LIFETIME
        // ===================================
        var now = DateTimeOffset.UtcNow;

        var expiresAt = now.AddMinutes(_jwtOptions.AccessTokenLifetimeMinutes);

        // ===================================
        // 4. STANDARD JWT CLAIMS
        // ===================================
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D")),

            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),

            new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)
        };

        // ===================================
        // 5. ROLE CLAIMS
        // ===================================
        foreach (var roleName in roleNames)
        {
            claims.Add(new Claim("role", roleName));
        }

        // ===================================
        // 6. CREATE SIGNED JWT
        // ===================================
        var jwtToken = new JwtSecurityToken(issuer: _jwtOptions.Issuer, audience: _jwtOptions.Audience, claims: claims, notBefore: now.UtcDateTime, expires: expiresAt.UtcDateTime, signingCredentials: _signingCredentials);

        // ===================================
        // 7. SERIALIZE JWT
        // ===================================
        var tokenHandler = new JwtSecurityTokenHandler();

        var accessToken = tokenHandler.WriteToken(jwtToken);

        // ===================================
        // 8. RETURN RESULT
        // ===================================
        return new AccessTokenResult(AccessToken: accessToken, TokenType: "Bearer", ExpiresIn: checked(_jwtOptions.AccessTokenLifetimeMinutes * 60));
    }
}
