using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Persistence.Seeding;
using Tindy.Application.Interfaces.Services;
using Tindy.Infrastructure.Services;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Repositories;
using Tindy.Infrastructure.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tindy.Infrastructure.Extensions;

public static class ServicesCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");


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
            options.UseAsyncSeeding(async (context, _, cancellationToken) =>
            {
                await TindySeedRunner.SeedAsync(context, cancellationToken);
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

        // External identity persistence (STEP 03)
        services.AddScoped<IExternalAuthRepository, ExternalAuthRepository>();

        services.AddScoped<IOAuthLoginTicketStore, OAuthLoginTicketStore>();
        services.AddScoped<IOAuthLinkTransactionStore, OAuthLinkTransactionStore>();
        services.AddScoped<IAccountConnectionsRepository, AccountConnectionsRepository>();

        // Onboarding repository
        services.AddScoped<IOnboardingRepository, OnboardingRepository>();

        // Read-only catalog repository
        services.AddScoped<ICatalogRepository, CatalogRepository>();

        // Profile projection repository (STEP 04).
        services.AddScoped<IProfileRepository, ProfileRepository>();

        // Experiences persistence (STEP 07).
        services.AddScoped<IExperienceRepository, ExperienceRepository>();

        // Certificate persistence (STEP 09).
        services.AddScoped<ICertificateRepository, CertificateRepository>();

        // Lightweight read-only check for protected business APIs
        services.AddScoped<IOnboardingAccessReader, OnboardingAccessReader>();

        // ===================================
        // JWT ACCESS TOKEN SERVICE
        // ===================================
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        services.Configure<BedrockOptions>(configuration.GetSection("Bedrock"));
        services.AddSingleton<BedrockProfileExtractionProvider>(provider =>
            new BedrockProfileExtractionProvider(
                new HttpClient(new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
                })
                { Timeout = Timeout.InfiniteTimeSpan },
                provider.GetRequiredService<IOptions<BedrockOptions>>(),
                provider.GetRequiredService<ILogger<BedrockProfileExtractionProvider>>()));

        services.AddSingleton<IProfileExtractionProvider>(provider => provider.GetRequiredService<BedrockProfileExtractionProvider>());
        services.AddSingleton<IAiScalarProfileProvider>(provider => provider.GetRequiredService<BedrockProfileExtractionProvider>());
        services.AddSingleton<IAiListExtractionProvider>(provider => provider.GetRequiredService<BedrockProfileExtractionProvider>());

        return services;
    }
}
