
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

        app.UseRouting();

        app.UseRateLimiter();
        
        // ===================================
        // 3. AUTHENTICATION
        // ===================================
        app.UseAuthentication();

        // ===================================
        // 4. AUTHORIZATION
        // ===================================
        app.UseAuthorization();

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
        }

        // ===================================
        // 6. MAP CONTROLLERS
        // ===================================
        app.MapControllers();

        return app;
    }
}
