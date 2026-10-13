
namespace Tindy.Api.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseApiPipeline(
        this WebApplication app)
    {
        // ===================================
        // 1. DEVELOPMENT TOOLS
        // ===================================
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();

            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint(
                    "/openapi/tindy.json",
                    "Tindy API");
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
            app.MapGet(
                "/api/v1/dev/auth-check",
                () => Results.Ok(new
                {
                    message = "Authenticated"
                }))
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
