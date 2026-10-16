using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Api.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Tindy.Infrastructure.Authentication;
using Tindy.Application.Interfaces.Services;
using Tindy.Application.Services;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace Tindy.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        // ===================================
        // 1. CONTROLLERS & OPENAPI
        // ===================================
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy =
                    JsonNamingPolicy.SnakeCaseLower;

                options.JsonSerializerOptions.DictionaryKeyPolicy =
                    JsonNamingPolicy.SnakeCaseLower;
            });


        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(entry => entry.Value is { Errors.Count: > 0 })
                    .ToDictionary(entry => JsonNamingPolicy.SnakeCaseLower.ConvertName(entry.Key), entry => entry.Value!.Errors.Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Invalid value." : error.ErrorMessage).ToArray());

                var problem = new ValidationProblemDetails(errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Bad Request",
                    Detail = "The input data is invalid.",
                    Instance = context.HttpContext.Request.Path
                };

                problem.Extensions["error_code"] =
                    "VALIDATION_FAILED";

                problem.Extensions["traceId"] =
                    context.HttpContext.TraceIdentifier;

                var result = new BadRequestObjectResult(problem);

                result.ContentTypes.Add("application/problem+json");

                return result;
            };
        });


        services.AddOpenApi("tindy");

        // ===================================
        // CORS CONFIGURATION
        // ===================================
        var allowedOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? Array.Empty<string>();

        services.AddCors(options =>
        {
            options.AddPolicy("TindyFrontend", policy =>
            {
                policy
                    .WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        // ===================================
        // 2. LOAD JWT SETTINGS
        // ===================================
        var jwtSection = configuration.GetSection(JwtOptions.SectionName);

        var jwtSettings = jwtSection.Get<JwtOptions>()
            ?? throw new InvalidOperationException("JWT configuration was not found.");

        // ===================================
        // 3. VALIDATE JWT SETTINGS
        // ===================================
        if (string.IsNullOrWhiteSpace(jwtSettings.Issuer) ||
            string.IsNullOrWhiteSpace(jwtSettings.Audience))
        {
            throw new InvalidOperationException("JWT Issuer and Audience are required.");
        }

        if (string.IsNullOrWhiteSpace(jwtSettings.SigningKey))
        {
            throw new InvalidOperationException("JWT SigningKey was not configured.");
        }

        byte[] signingKeyBytes;

        try
        {
            signingKeyBytes = Convert.FromBase64String(jwtSettings.SigningKey);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("JWT SigningKey must be valid Base64.", ex);
        }

        if (signingKeyBytes.Length < 32)
        {
            throw new InvalidOperationException("JWT SigningKey must contain at least 32 bytes.");
        }

        if (jwtSettings.AccessTokenLifetimeMinutes <= 0 ||
            jwtSettings.ClockSkewSeconds < 0)
        {
            throw new InvalidOperationException("JWT lifetime or clock skew is invalid.");
        }

        // Shared configuration for JWT Token Service
        services.Configure<JwtOptions>(jwtSection);

        // ===================================
        // 4. JWT BEARER AUTHENTICATION
        // ===================================
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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

                        ClockSkew = TimeSpan.FromSeconds(jwtSettings.ClockSkewSeconds),

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

                        await WriteAuthenticationProblemAsync(context.HttpContext, StatusCodes.Status401Unauthorized, "Unauthorized", "Missing, invalid or expired access token.", "AUTH_UNAUTHORIZED");
                    },

                    OnForbidden = context =>
                        WriteAuthenticationProblemAsync(context.HttpContext, StatusCodes.Status403Forbidden, "Forbidden", "You do not have permission to access this resource.", "AUTH_FORBIDDEN")
                };
            })
            // OAuth is opt-in and uses a dedicated temporary cookie.
            // The default authentication scheme stays JWT Bearer.
            .AddTindyExternalOAuth(configuration);

        // ===================================
        // 6. AUTHORIZATION
        // ===================================
        // Every API is protected by default, including future business APIs.
        // Explicitly exempt only authenticated setup endpoints and public endpoints.
        services.AddAuthorization(options =>
        {
            var completedOnboardingPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new OnboardingCompletedRequirement())
                .Build();

            // Plain [Authorize] uses this policy.
            options.DefaultPolicy = completedOnboardingPolicy;

            // Routes without any [Authorize] also require onboarding unless
            // explicitly decorated with [AllowAnonymous] or an exception policy.
            options.FallbackPolicy = completedOnboardingPolicy;

            // Limited access for /me, onboarding and logout.
            options.AddPolicy(OnboardingPolicies.AuthenticatedOnly, new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser().Build());
        });

        services.AddScoped<IAuthorizationHandler, OnboardingCompletedHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, OnboardingAuthorizationResultHandler>();

        // ===================================
        // AUTHENTICATION APPLICATION SERVICE
        // ===================================
        services.AddScoped<IAuthService, AuthService>();

        // External identity business logic (STEP 03, no OAuth endpoints yet)
        services.AddScoped<IExternalAuthService, ExternalAuthService>();

        services.AddScoped<IOAuthLoginFlowService, OAuthLoginFlowService>();

        // Onboarding application service
        services.AddScoped<IOnboardingService, OnboardingService>();

        // Read-only catalog application service (available before onboarding completion)
        services.AddScoped<ICatalogService, CatalogService>();




        // ===================================
        // LOGIN RATE LIMITING
        // ===================================
        services.AddRateLimiter(options =>
        {
            // HTTP Status Code
            options.RejectionStatusCode =
                StatusCodes.Status429TooManyRequests;

            // ===================================
            // 1. LOGIN RATE LIMIT POLICY
            // ===================================
            options.AddPolicy("auth-login", context => RateLimitPartition.GetFixedWindowLimiter(partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown", factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

            // Limit start/challenge redirects as well as ticket-exchange attempts.
            options.AddPolicy("oauth-start", context => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20, Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0, AutoReplenishment = true
                }));

            // OAuth ticket exchange: rate-limit invalid/replayed ticket attempts.
            options.AddPolicy("oauth-exchange", context => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10, Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0, AutoReplenishment = true
                }));

            // ===================================
            // 2. CUSTOM 429 ERROR RESPONSE
            // ===================================
            options.OnRejected = async (context, cancellationToken) =>
            {
                var httpContext = context.HttpContext;

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too Many Requests",
                    Detail = "Too many login attempts. Please try again later.",
                    Instance = httpContext.Request.Path.ToString()
                };

                problem.Extensions["error_code"] =
                    "AUTH_TOO_MANY_ATTEMPTS";

                problem.Extensions["traceId"] =
                    httpContext.TraceIdentifier;

                httpContext.Response.StatusCode =
                    StatusCodes.Status429TooManyRequests;

                httpContext.Response.ContentType =
                    "application/problem+json";

                await httpContext.Response.WriteAsync(JsonSerializer.Serialize(problem), cancellationToken);
            };
        });



        return services;
    }

    // ===================================
    // 7. AUTH ERROR RESPONSE HELPER
    // ===================================
    private static async Task WriteAuthenticationProblemAsync(HttpContext httpContext, int statusCode, string title, string detail, string errorCode)
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

        await httpContext.Response.WriteAsync(JsonSerializer.Serialize(problemDetails), httpContext.RequestAborted);
    }
}
