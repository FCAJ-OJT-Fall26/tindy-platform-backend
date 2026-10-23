using System.Text.Json;

namespace Tindy.Infrastructure.Media;

public sealed record GuardDutyScanEvent(string EventId, DateTime Time, string Bucket, string Key, string ETag, string? VersionId,
    string ScanStatus, string Result)
{
    public static GuardDutyScanEvent? Parse(string body, GuardDutyOptions options)
    {
        if (body.Length > 65_536) return null;
        try
        {
            using var doc = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            var root = doc.RootElement;
            if (HasDuplicates(root) || Text(root, "version") != "0" || Text(root, "source") != "aws.guardduty" ||
                Text(root, "detail-type") != "GuardDuty Malware Protection Object Scan Result" ||
                Text(root, "account") != options.AccountId || Text(root, "region") != options.Region ||
                !Guid.TryParse(Text(root, "id"), out _) ||
                !root.GetProperty("resources").EnumerateArray().Any(r => r.GetString() == options.ProtectionPlanArn)) return null;
            var time = root.GetProperty("time").GetDateTimeOffset().UtcDateTime;
            if (time > DateTime.UtcNow.AddMinutes(5)) return null;
            var detail = root.GetProperty("detail");
            if (Text(detail, "schemaVersion") != "1.0" || Text(detail, "resourceType") != "S3_OBJECT") return null;
            var obj = detail.GetProperty("s3ObjectDetails");
            var bucket = Text(obj, "bucketName");
            var key = Text(obj, "objectKey");
            var tag = Text(obj, "eTag");
            if (bucket != options.BucketName || string.IsNullOrWhiteSpace(tag) || key is null ||
                !key.StartsWith("certificates/pdf/", StringComparison.Ordinal)) return null;
            Storage.StorageObjectKeys.Validate(key);
            var status = Text(detail, "scanStatus");
            var result = Text(detail.GetProperty("scanResultDetails"), "scanResultStatus");
            if (!((status == "COMPLETED" && result is "NO_THREATS_FOUND" or "THREATS_FOUND") ||
                  (status == "SKIPPED" && result is "UNSUPPORTED" or "ACCESS_DENIED") ||
                  (status == "FAILED" && result == "FAILED"))) return null;
            return new(Text(root, "id")!, time, bucket!, key, tag, Text(obj, "versionId"), status!, result!);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool HasDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array) return element.EnumerateArray().Any(HasDuplicates);
        if (element.ValueKind != JsonValueKind.Object) return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        return element.EnumerateObject().Any(p => !names.Add(p.Name) || HasDuplicates(p.Value));
    }
}
