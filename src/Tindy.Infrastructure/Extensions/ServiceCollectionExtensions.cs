using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Persistence.Seeding;
using Tindy.Application.Interfaces.Services;
using Tindy.Infrastructure.Services;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Repositories;

namespace Tindy.Infrastructure.Extensions;

public static class ServicesCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string ' 'DefaultConnection' was not found'.");


        services.AddDbContext<TindyDbContext>(options =>
        {
            // PostgreSQL
            options.UseNpgsql(connectionString);

            // ===================================
            // SYNCHRONOUS DATABASE SEEDING
            // ===================================
            options.UseSeeding((context, _) =>
            {
                TindySeedRunner.Seed(context);
            });

            // ===================================
            // ASYNCHRONOUS DATABASE SEEDING
            // ===================================
            options.UseAsyncSeeding(
                async (context, _, cancellationToken) =>
                {
                    await TindySeedRunner.SeedAsync(
                        context,
                        cancellationToken);
                });
        });

        // ===================================
        // PASSWORD HASHING
        // ===================================
        services.AddScoped<IPasswordHasher, PasswordHasher>();

        // ===================================
        // AUTHENTICATION REPOSITORY
        // ===================================
        services.AddScoped<IAuthRepository, AuthRepository>();

        // ===================================
        // JWT ACCESS TOKEN SERVICE
        // ===================================
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        return services;
    }
}