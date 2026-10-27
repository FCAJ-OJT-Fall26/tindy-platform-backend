
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Tindy.Api.Authorization;

namespace Tindy.Api.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        // ===================================
        // 1. DEVELOPMENT TOOLS
        // ===================================
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi().AllowAnonymous();

            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/tindy.json", "Tindy API");
            });
        }

        // ===================================
        // 2. HTTPS REDIRECTION
        // ===================================
        app.UseHttpsRedirection();

        // ===================================
        // 3. ROUTING
        // ===================================
        app.UseRouting();

        // ===================================
        // 4. CORS
        // ===================================
        app.UseCors("TindyFrontend");
        app.UseMiddleware<Tindy.Api.Middleware.AiExtractionDeadlineMiddleware>();

        // ===================================
        // 5. AUTHENTICATION
        // ===================================
        app.UseAuthentication();

        // ===================================
        // 6. AUTHORIZATION
        // ===================================
        app.UseAuthorization();

        // Resolve the authenticated, active account before per-user quotas.
        app.UseRateLimiter();

        // ===================================
        // 5. DEVELOPMENT AUTH TEST
        // ===================================
        if (app.Environment.IsDevelopment())
        {
            // Keep the JWT-only smoke test working for new users.
            app.MapGet("/api/dev/auth-check", () => Results.Ok(new { message = "Authenticated" }))
                .RequireAuthorization(OnboardingPolicies.AuthenticatedOnly)
                .ExcludeFromDescription();

            // DEV-only test for the full onboarding gate.
            // This endpoint uses DefaultPolicy (JWT + completed onboarding).
            app.MapGet("/api/dev/onboarding-gate-check", () => Results.Ok(new { message = "Onboarding gate passed" }))
                .RequireAuthorization()
                .ExcludeFromDescription();

            // STEP 02: Development-only handshake smoke tests (NO Tindy JWT issued).
            app.MapGet("/api/dev/oauth/google/start", (HttpContext context) =>
                    StartOAuthHandshakeAsync(context, OAuthAuthenticationExtensions.GoogleScheme))
                .AllowAnonymous()
                .ExcludeFromDescription();

            app.MapGet("/api/dev/oauth/github/start", (HttpContext context) =>
                    StartOAuthHandshakeAsync(context, OAuthAuthenticationExtensions.GitHubScheme))
                .AllowAnonymous()
                .ExcludeFromDescription();

            app.MapGet("/api/dev/oauth/result", async (HttpContext context) =>
                {
                    context.Response.Headers["Cache-Control"] = "no-store";
                    var result = await context.AuthenticateAsync(OAuthAuthenticationExtensions.ExternalCookieScheme);
                    if (!result.Succeeded)
                    {
                        return Results.Problem(
                            title: "OAuth handshake was not completed.",
                            statusCode: StatusCodes.Status401Unauthorized);
                    }

                    await context.SignOutAsync(OAuthAuthenticationExtensions.ExternalCookieScheme);
                    return Results.Ok(new
                    {
                        success = true,
                        message = "External OAuth handshake completed. No Tindy user or JWT was created."
                    });
                })
                .AllowAnonymous()
                .ExcludeFromDescription();
        }

        // ===================================
        // 6. MAP CONTROLLERS
        // ===================================
        app.MapControllers();

        return app;
    }

    private static async Task StartOAuthHandshakeAsync(HttpContext context, string provider)
    {
        var availableSchemes = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
        if (await availableSchemes.GetSchemeAsync(provider) is null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                message = "OAuth provider is not configured for this environment."
            });
            return;
        }

        await context.ChallengeAsync(provider, new AuthenticationProperties
        {
            RedirectUri = "/api/dev/oauth/result"
        });
    }
}
