
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Tindy.Infrastructure.Authentication;

namespace Tindy.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ===================================
        // 1. CONTROLLERS & OPENAPI
        // ===================================
        services.AddControllers();

        services.AddOpenApi("tindy");

        // ===================================
        // 2. LOAD JWT SETTINGS
        // ===================================
        var jwtSection = configuration.GetSection(
            JwtOptions.SectionName);

        var jwtSettings = jwtSection.Get<JwtOptions>()
            ?? throw new InvalidOperationException(
                "JWT configuration was not found.");

        // ===================================
        // 3. VALIDATE JWT SETTINGS
        // ===================================
        if (string.IsNullOrWhiteSpace(jwtSettings.Issuer) ||
            string.IsNullOrWhiteSpace(jwtSettings.Audience))
        {
            throw new InvalidOperationException(
                "JWT Issuer and Audience are required.");
        }

        if (string.IsNullOrWhiteSpace(jwtSettings.SigningKey))
        {
            throw new InvalidOperationException(
                "JWT SigningKey was not configured.");
        }

        byte[] signingKeyBytes;

        try
        {
            signingKeyBytes = Convert.FromBase64String(
                jwtSettings.SigningKey);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "JWT SigningKey must be valid Base64.",
                ex);
        }

        if (signingKeyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                "JWT SigningKey must contain at least 32 bytes.");
        }

        if (jwtSettings.AccessTokenLifetimeMinutes <= 0 ||
            jwtSettings.ClockSkewSeconds < 0)
        {
            throw new InvalidOperationException(
                "JWT lifetime or clock skew is invalid.");
        }

        // Shared configuration for JWT Token Service
        services.Configure<JwtOptions>(jwtSection);

        // ===================================
        // 4. JWT BEARER AUTHENTICATION
        // ===================================
        services
            .AddAuthentication(
                JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.SaveToken = false;

                options.TokenValidationParameters =
                    new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = jwtSettings.Issuer,

                        ValidateAudience = true,
                        ValidAudience = jwtSettings.Audience,

                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey =
                            new SymmetricSecurityKey(signingKeyBytes),

                        RequireSignedTokens = true,

                        ValidAlgorithms = new[]
                        {
                            SecurityAlgorithms.HmacSha256
                        },

                        ValidateLifetime = true,
                        RequireExpirationTime = true,

                        ClockSkew = TimeSpan.FromSeconds(
                            jwtSettings.ClockSkewSeconds),

                        NameClaimType = "sub",
                        RoleClaimType = "role"
                    };

                // ===================================
                // 5. AUTHENTICATION ERROR HANDLING
                // ===================================
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();

                        context.Response.Headers[
                            "WWW-Authenticate"] = "Bearer";

                        await WriteAuthenticationProblemAsync(
                            context.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            "Unauthorized",
                            "Missing, invalid or expired access token.",
                            "AUTH_UNAUTHORIZED");
                    },

                    OnForbidden = context =>
                        WriteAuthenticationProblemAsync(
                            context.HttpContext,
                            StatusCodes.Status403Forbidden,
                            "Forbidden",
                            "You do not have permission to access this resource.",
                            "AUTH_FORBIDDEN")
                };
            });

        // ===================================
        // 6. AUTHORIZATION
        // ===================================
        services.AddAuthorization();

        return services;
    }

    // ===================================
    // 7. AUTH ERROR RESPONSE HELPER
    // ===================================
    private static async Task WriteAuthenticationProblemAsync(
        HttpContext httpContext,
        int statusCode,
        string title,
        string detail,
        string errorCode)
    {
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path.ToString()
        };

        problemDetails.Extensions["error_code"] = errorCode;

        problemDetails.Extensions["traceId"] =
            httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = statusCode;

        httpContext.Response.ContentType =
            "application/problem+json";

        await httpContext.Response.WriteAsync(
            JsonSerializer.Serialize(problemDetails),
            httpContext.RequestAborted);
    }
}
