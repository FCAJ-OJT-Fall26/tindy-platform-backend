using Amazon;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Infrastructure.Storage;

public static class MediaStorageServiceRegistration
{
    public static IServiceCollection AddMediaStorage(this IServiceCollection services, IConfiguration configuration)
    {
        // Lazy factory: keeps existing auth/profile startup unchanged until media is used.
        services.AddSingleton<IFileStorageService>(sp =>
        {
            var options = configuration.GetSection(MediaStorageOptions.SectionName).Get<MediaStorageOptions>()
                ?? throw new InvalidOperationException("MediaStorage configuration is missing.");
            var host = sp.GetRequiredService<IHostEnvironment>();
            switch (options.Provider.Trim().ToLowerInvariant())
            {
                case "local":
                    return new LocalFileStorageService(options.Local.RootPath, host.ContentRootPath);
                case "s3":
                    if (string.IsNullOrWhiteSpace(options.S3.Region) || string.IsNullOrWhiteSpace(options.S3.BucketName))
                        throw new InvalidOperationException("MediaStorage:S3:Region and BucketName are required.");
                    var client = new AmazonS3Client(new AmazonS3Config
                    {
                        RegionEndpoint = RegionEndpoint.GetBySystemName(options.S3.Region)
                    }); // AWS default credential chain: local profile, env, EC2/ECS role.
                    return new S3FileStorageService(client, options.S3.BucketName, ownsClient: true);
                default:
                    throw new InvalidOperationException("MediaStorage:Provider must be 'local' or 's3'.");
            }
        });
        return services;
    }
}
