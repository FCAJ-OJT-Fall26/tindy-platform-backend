namespace Tindy.Api.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseApiPipeline(
        this WebApplication app)
    {
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

        app.UseHttpsRedirection();

        app.MapControllers();

        return app;
    }
}