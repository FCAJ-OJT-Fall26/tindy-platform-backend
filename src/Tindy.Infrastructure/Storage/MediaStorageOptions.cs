namespace Tindy.Infrastructure.Storage;

public sealed class MediaStorageOptions
{
    public const string SectionName = "MediaStorage";
    public string Provider { get; set; } = "";
    public LocalStorageOptions Local { get; set; } = new();
    public S3StorageOptions S3 { get; set; } = new();
}

public sealed class LocalStorageOptions
{
    public string RootPath { get; set; } = "";
}

public sealed class S3StorageOptions
{
    public string BucketName { get; set; } = "";
    public string Region { get; set; } = "";
}
