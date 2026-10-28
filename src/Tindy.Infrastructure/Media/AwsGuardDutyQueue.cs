using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Tindy.Infrastructure.Media;

public sealed class AwsGuardDutyQueue(IAmazonSQS sqs, IOptions<GuardDutyOptions> options) : IGuardDutyQueue
{
    private bool _validated;

    public async Task<IReadOnlyList<ScanQueueMessage>> ReceiveAsync(CancellationToken ct)
    {
        if (!_validated) await ValidateRedriveAsync(ct);
        var response = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = options.Value.QueueUrl,
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 20,
            VisibilityTimeout = 120
        }, ct);
        return response.Messages?.Select(m => new ScanQueueMessage(m.Body, m.ReceiptHandle)).ToArray() ?? [];
    }

    public async Task DeleteAsync(string receiptHandle, CancellationToken ct) =>
        await sqs.DeleteMessageAsync(new DeleteMessageRequest { QueueUrl = options.Value.QueueUrl, ReceiptHandle = receiptHandle }, ct);

    private async Task ValidateRedriveAsync(CancellationToken ct)
    {
        var response = await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
        {
            QueueUrl = options.Value.QueueUrl, AttributeNames = ["RedrivePolicy"]
        }, ct);
        if (response.Attributes is null || !response.Attributes.TryGetValue("RedrivePolicy", out var policy))
            throw new InvalidOperationException("GuardDuty SQS queue requires a redrive policy and DLQ.");
        using var doc = JsonDocument.Parse(policy);
        var target = doc.RootElement.GetProperty("deadLetterTargetArn").GetString();
        var attempts = doc.RootElement.GetProperty("maxReceiveCount");
        var count = attempts.ValueKind == JsonValueKind.String ? int.Parse(attempts.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : attempts.GetInt32();
        if (target is null || !target.StartsWith($"arn:aws:sqs:{options.Value.Region}:{options.Value.AccountId}:", StringComparison.Ordinal) || count is < 1 or > 10)
            throw new InvalidOperationException("GuardDuty SQS redrive policy must use an account-local DLQ and 1-10 receive attempts.");
        _validated = true;
    }
}
