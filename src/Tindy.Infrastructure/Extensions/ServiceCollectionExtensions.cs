using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Persistence.Seeding;
using Tindy.Application.Interfaces.Services;
using Tindy.Infrastructure.Services;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Repositories;
using Tindy.Infrastructure.Storage;
using Tindy.Infrastructure.Media;

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

        // STEP 02: storage is resolved only when a media feature requests it.
        services.AddMediaStorage(configuration);
        services.AddMediaProcessing(configuration);

        // STEP 03 media; only accessed after authorization and onboarding.
        services.AddScoped<IProfileMediaService, ProfileMediaService>();
        services.AddScoped<ICertificateAttachmentRepository, CertificateAttachmentRepository>();
        services.AddScoped<ICertificateAttachmentService, Tindy.Application.Services.CertificateAttachmentService>();
        services.AddSingleton<ICertificateFileProcessor, CertificateFileProcessor>();
        services.AddHttpClient("tindy-provider-avatar", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TindyMedia/1.0");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false
        });
        return services;
    }
}
