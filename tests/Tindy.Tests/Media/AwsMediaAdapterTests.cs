using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using Tindy.Infrastructure.Media;
using Tindy.Infrastructure.Storage;
using Xunit;

namespace Tindy.Tests.Media;

// SDK method overrides exercise adapter requests without connecting to AWS.
public sealed class AwsMediaAdapterTests
{
    [Theory]
    [InlineData(null, "stored-version", 1)]
    [InlineData("explicit-version", "explicit-version", 0)]
    public async Task S3Cleanup_DeletesAnExactVersionInsteadOfOnlyCreatingDeleteMarker(string? version, string expected, int heads)
    {
        using var client = new S3Double();
        using var storage = new S3FileStorageService(client, "private-test");
        await storage.DeleteAsync(Key(), version);
        Assert.Equal(heads, client.HeadCalls);
        Assert.Equal(expected, client.DeletedVersion);
    }

    [Fact]
    public async Task S3Cleanup_NotFoundIsIdempotent_ButOtherDeleteErrorsPropagate()
    {
        using var client = new S3Double { Missing = true };
        using var storage = new S3FileStorageService(client, "private-test");
        await storage.DeleteAsync(Key());
        Assert.Null(client.DeletedVersion);
        client.Missing = false;
        client.FailDelete = true;
        await Assert.ThrowsAsync<IOException>(() => storage.DeleteAsync(Key(), "version-1"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SqsAdapter_RequiresDlqBeforeReceiving_AndUsesBoundedVisibility(bool validPolicy)
    {
        using var client = new SqsDouble { ValidPolicy = validPolicy };
        var queue = new AwsGuardDutyQueue(client, Options.Create(GuardDutyEventTests.Configuration()));
        if (!validPolicy)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => queue.ReceiveAsync(default));
            Assert.Null(client.Received);
            return;
        }
        Assert.Single(await queue.ReceiveAsync(default));
        Assert.Equal(1, client.Received!.MaxNumberOfMessages);
        Assert.Equal(20, client.Received.WaitTimeSeconds);
        Assert.Equal(120, client.Received.VisibilityTimeout);
        await queue.DeleteAsync("receipt", default);
        Assert.Equal("receipt", client.DeletedReceipt);
    }

    private static string Key() => $"certificates/pdf/{Guid.NewGuid():N}/{Guid.NewGuid():N}.pdf";

    private sealed class S3Double() : AmazonS3Client(new AnonymousAWSCredentials(), new AmazonS3Config { RegionEndpoint = Amazon.RegionEndpoint.USEast1 })
    {
        public bool Missing { get; set; }
        public bool FailDelete { get; set; }
        public int HeadCalls { get; private set; }
        public string? DeletedVersion { get; private set; }
        public override Task<GetObjectMetadataResponse> GetObjectMetadataAsync(GetObjectMetadataRequest request, CancellationToken cancellationToken = default)
        {
            HeadCalls++;
            if (Missing) throw new AmazonS3Exception("Not found") { StatusCode = HttpStatusCode.NotFound };
            return Task.FromResult(new GetObjectMetadataResponse { VersionId = "stored-version" });
        }
        public override Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request, CancellationToken cancellationToken = default)
        {
            if (FailDelete) throw new IOException("Transient failure.");
            DeletedVersion = request.VersionId;
            return Task.FromResult(new DeleteObjectResponse());
        }
    }

    private sealed class SqsDouble() : AmazonSQSClient(new AnonymousAWSCredentials(), new AmazonSQSConfig { RegionEndpoint = Amazon.RegionEndpoint.USEast1 })
    {
        public bool ValidPolicy { get; init; }
        public ReceiveMessageRequest? Received { get; private set; }
        public string? DeletedReceipt { get; private set; }
        public override Task<GetQueueAttributesResponse> GetQueueAttributesAsync(GetQueueAttributesRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GetQueueAttributesResponse
            {
                Attributes = ValidPolicy ? new Dictionary<string, string>
                {
                    ["RedrivePolicy"] = "{\"deadLetterTargetArn\":\"arn:aws:sqs:ap-southeast-1:111122223333:test-dlq\",\"maxReceiveCount\":\"10\"}"
                } : []
            });
        public override Task<ReceiveMessageResponse> ReceiveMessageAsync(ReceiveMessageRequest request, CancellationToken cancellationToken = default)
        {
            Received = request;
            return Task.FromResult(new ReceiveMessageResponse { Messages = [new Message { Body = "event", ReceiptHandle = "receipt" }] });
        }
        public override Task<DeleteMessageResponse> DeleteMessageAsync(DeleteMessageRequest request, CancellationToken cancellationToken = default)
        {
            DeletedReceipt = request.ReceiptHandle;
            return Task.FromResult(new DeleteMessageResponse());
        }
    }
}
