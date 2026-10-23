using Amazon;
using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Application.Interfaces.Services;
using Tindy.Infrastructure.Storage;

namespace Tindy.Infrastructure.Media;

public static class MediaProcessingRegistration
{
    public static IServiceCollection AddMediaProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ClamAvOptions>().Bind(configuration.GetSection("ClamAv"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Host) && o.Port is > 0 and <= 65535 && o.TimeoutSeconds is >= 1 and <= 60,
                "ClamAv host, port or timeout is invalid.").ValidateOnStart();
        services.AddOptions<MediaLifecycleOptions>().Bind(configuration.GetSection("MediaLifecycle"))
            .Validate(o => o.PollIntervalSeconds is >= 1 and <= 300 && o.BatchSize is >= 1 and <= 100 && o.RejectedRetentionHours is >= 0 and <= 23,
                "Media lifecycle polling, batch size or retention is invalid.").ValidateOnStart();
        services.AddSingleton<IFileMalwareScanner, ClamAvMalwareScanner>();
        services.AddScoped<MediaLifecycleService>();
        services.AddSingleton<MediaOrphanSchedule>();
        services.AddScoped<MediaOrphanReconciler>();
        if (configuration.GetValue("MediaLifecycle:Enabled", true)) services.AddHostedService<MediaLifecycleWorker>();

        services.AddOptions<GuardDutyOptions>().Bind(configuration.GetSection("GuardDuty"))
            .Validate(o => !o.Enabled || ValidGuardDuty(o, configuration), "GuardDuty requires matching S3 storage, region, account, protection plan ARN and an HTTPS SQS queue URL.")
            .ValidateOnStart();
        services.AddScoped<IGuardDutyEventHandler, GuardDutyEventHandler>();
        if (configuration.GetValue<bool>("GuardDuty:Enabled"))
        {
            // Never resolve AWS credentials or create a client for Local Development.
            services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient(new AmazonSQSConfig
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(configuration["GuardDuty:Region"]!),
                Timeout = TimeSpan.FromSeconds(40), MaxErrorRetry = 2
            }));
            services.AddSingleton<IGuardDutyQueue, AwsGuardDutyQueue>();
            services.AddHostedService<GuardDutyQueueWorker>();
        }
        return services;
    }

    private static bool ValidGuardDuty(GuardDutyOptions o, IConfiguration configuration)
    {
        if (configuration[$"{MediaStorageOptions.SectionName}:Provider"] != "s3" ||
            configuration[$"{MediaStorageOptions.SectionName}:S3:BucketName"] != o.BucketName ||
            configuration[$"{MediaStorageOptions.SectionName}:S3:Region"] != o.Region ||
            o.AccountId.Length != 12 || !o.AccountId.All(char.IsAsciiDigit) || string.IsNullOrWhiteSpace(o.Region) ||
            string.IsNullOrWhiteSpace(o.BucketName)) return false;
        var arnPrefix = $"arn:aws:guardduty:{o.Region}:{o.AccountId}:malware-protection-plan/";
        if (!o.ProtectionPlanArn.StartsWith(arnPrefix, StringComparison.Ordinal) || o.ProtectionPlanArn.Length <= arnPrefix.Length) return false;
        return Uri.TryCreate(o.QueueUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort &&
            string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
            uri.Host == $"sqs.{o.Region}.amazonaws.com" && uri.AbsolutePath.StartsWith($"/{o.AccountId}/", StringComparison.Ordinal) &&
            uri.AbsolutePath.Length > o.AccountId.Length + 2;
    }
}
