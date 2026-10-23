using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tindy.Infrastructure.Media;
using Xunit;

namespace Tindy.Tests.Media;

public sealed class GuardDutyEventTests
{
    internal static GuardDutyOptions Configuration() => new()
    {
        Enabled = true, AccountId = "111122223333", Region = "ap-southeast-1", BucketName = "tindy-test-private",
        ProtectionPlanArn = "arn:aws:guardduty:ap-southeast-1:111122223333:malware-protection-plan/test-plan",
        QueueUrl = "https://sqs.ap-southeast-1.amazonaws.com/111122223333/test-results"
    };

    internal static string Event(string key, string verdict = "NO_THREATS_FOUND", string? version = "version-1", string etag = "test-etag") =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["version"] = "0", ["id"] = Guid.NewGuid().ToString(), ["source"] = "aws.guardduty",
            ["detail-type"] = "GuardDuty Malware Protection Object Scan Result", ["account"] = Configuration().AccountId,
            ["region"] = Configuration().Region, ["time"] = DateTime.UtcNow,
            ["resources"] = new[] { Configuration().ProtectionPlanArn },
            ["detail"] = new
            {
                schemaVersion = "1.0", resourceType = "S3_OBJECT",
                scanStatus = verdict switch { "UNSUPPORTED" or "ACCESS_DENIED" => "SKIPPED", "FAILED" => "FAILED", _ => "COMPLETED" },
                s3ObjectDetails = new { bucketName = Configuration().BucketName, objectKey = key, eTag = etag, versionId = version, s3Throttled = false },
                scanResultDetails = new { scanResultStatus = verdict }
            }
        });

    [Theory]
    [InlineData("source", "evil")]
    [InlineData("account", "000000000000")]
    [InlineData("region", "other-region")]
    [InlineData("version", "1")]
    [InlineData("detail-type", "GuardDuty Finding")]
    [InlineData("id", "invalid")]
    public void InvalidEnvelope_IsRejected(string field, string value)
    {
        var node = JsonNode.Parse(Event(Key()))!;
        node[field] = value;
        Assert.Null(GuardDutyScanEvent.Parse(node.ToJsonString(), Configuration()));
    }

    [Fact]
    public void InvalidPayload_Plan_Bucket_Key_AndScanStatus_AreRejected()
    {
        Assert.Null(GuardDutyScanEvent.Parse("bad-json", Configuration()));
        Assert.Null(GuardDutyScanEvent.Parse("{}", Configuration()));
        foreach (var field in new[] { "bucketName", "objectKey", "eTag" })
        {
            var node = JsonNode.Parse(Event(Key()))!;
            node["detail"]!["s3ObjectDetails"]![field] = field == "objectKey" ? "../../file.pdf" : "";
            Assert.Null(GuardDutyScanEvent.Parse(node.ToJsonString(), Configuration()));
        }
        var invalid = JsonNode.Parse(Event(Key()))!;
        invalid["resources"] = new JsonArray("untrusted-plan");
        Assert.Null(GuardDutyScanEvent.Parse(invalid.ToJsonString(), Configuration()));
        invalid = JsonNode.Parse(Event(Key()))!;
        invalid["detail"]!["scanStatus"] = "FAILED";
        Assert.Null(GuardDutyScanEvent.Parse(invalid.ToJsonString(), Configuration()));
        Assert.Null(GuardDutyScanEvent.Parse(Event(Key(), "UNKNOWN"), Configuration()));
    }

    [Theory]
    [InlineData(ScanEventDisposition.Processed, true)]
    [InlineData(ScanEventDisposition.Ignored, true)]
    [InlineData(ScanEventDisposition.Retry, false)]
    [InlineData(ScanEventDisposition.Invalid, false)]
    public async Task Queue_OnlyAcknowledgesCommittedOrSafelyIgnoredEvents(ScanEventDisposition disposition, bool acknowledge)
    {
        var queue = new QueueDouble();
        using var services = new ServiceCollection().AddScoped<IGuardDutyEventHandler>(_ => new HandlerDouble(disposition)).BuildServiceProvider();
        using var worker = new GuardDutyQueueWorker(queue, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<GuardDutyQueueWorker>.Instance);
        await worker.RunOnceAsync(default);
        Assert.Equal(acknowledge ? 1 : 0, queue.Deleted);
    }

    [Fact]
    public async Task TransientHandlerError_DoesNotDeleteMessage()
    {
        var queue = new QueueDouble();
        using var services = new ServiceCollection().AddScoped<IGuardDutyEventHandler>(_ => new HandlerDouble(ScanEventDisposition.Processed, true)).BuildServiceProvider();
        using var worker = new GuardDutyQueueWorker(queue, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<GuardDutyQueueWorker>.Instance);
        await worker.RunOnceAsync(default);
        Assert.Equal(0, queue.Deleted);
    }

    [Fact]
    public async Task QueueWorker_ShutsDownDuringLongPolling()
    {
        var queue = new QueueDouble { Block = true };
        using var services = new ServiceCollection().BuildServiceProvider();
        using var worker = new GuardDutyQueueWorker(queue, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<GuardDutyQueueWorker>.Instance);
        await worker.StartAsync(default);
        await queue.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StopAsync(timeout.Token);
        Assert.True(queue.Cancelled);
        Assert.Equal(0, queue.Deleted);
    }

    private static string Key() => $"certificates/pdf/{Guid.NewGuid():N}/{Guid.NewGuid():N}.pdf";

    private sealed class HandlerDouble(ScanEventDisposition disposition, bool fail = false) : IGuardDutyEventHandler
    {
        public Task<ScanEventDisposition> ProcessAsync(string body, CancellationToken ct) => fail
            ? Task.FromException<ScanEventDisposition>(new IOException("Transient failure.")) : Task.FromResult(disposition);
    }

    private sealed class QueueDouble : IGuardDutyQueue
    {
        public int Deleted { get; private set; }
        public bool Block { get; init; }
        public bool Cancelled { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<ScanQueueMessage>> ReceiveAsync(CancellationToken ct)
        {
            Entered.TrySetResult();
            if (Block)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                catch (OperationCanceledException) { Cancelled = true; throw; }
            }
            return [new("body", "receipt")];
        }
        public Task DeleteAsync(string receiptHandle, CancellationToken ct) { Deleted++; return Task.CompletedTask; }
    }
}
