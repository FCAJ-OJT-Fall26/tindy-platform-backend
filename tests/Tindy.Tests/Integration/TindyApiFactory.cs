using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Persistence.Seeding;
using Xunit;

namespace Tindy.Tests.Integration;

/// <summary>
/// A private throwaway PostgreSQL database and real ASP.NET Core TestServer.
/// Never connects to the developer's PostgreSQL instance.
/// Requires Docker Desktop / Docker Engine running.
/// </summary>
public sealed class TindyApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("tindy_integration_tests")
        .WithUsername("postgres")
        .WithPassword("postgres_test_only")
        .Build();

    private readonly string _jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "Tindy.Api",
                ["Jwt:Audience"] = "Tindy.Client",
                ["Jwt:AccessTokenLifetimeMinutes"] = "60",
                ["Jwt:ClockSkewSeconds"] = "0",
                ["Jwt:SigningKey"] = _jwtKey,
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString()
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<TindyDbContext>>();
            services.RemoveAll<TindyDbContext>();
            services.AddDbContext<TindyDbContext>(options => options.UseNpgsql(_postgres.GetConnectionString()));
        });
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Starting the host is intentional only after PostgreSQL is ready.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        await db.Database.MigrateAsync();
        // The test DbContext overrides production EF options: seed explicitly.
        await TindySeedRunner.SeedAsync(db);
    }

    Task IAsyncLifetime.DisposeAsync() => StopAsync();

    private async Task StopAsync()
    {
        Dispose();
        await _postgres.DisposeAsync();
    }

    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });
}
