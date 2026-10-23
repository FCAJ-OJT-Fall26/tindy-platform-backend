namespace Tindy.Infrastructure.Media;

public sealed class ClamAvOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3310;
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class MediaLifecycleOptions
{
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 50;
    public int RejectedRetentionHours { get; set; } = 1;
}

public sealed class GuardDutyOptions
{
    public bool Enabled { get; set; }
    public string Region { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string BucketName { get; set; } = "";
    public string ProtectionPlanArn { get; set; } = "";
    public string QueueUrl { get; set; } = "";
}
