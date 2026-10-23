using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tindy.Infrastructure.Media;

public sealed record ScanQueueMessage(string Body, string ReceiptHandle);

public interface IGuardDutyQueue
{
    Task<IReadOnlyList<ScanQueueMessage>> ReceiveAsync(CancellationToken ct);
    Task DeleteAsync(string receiptHandle, CancellationToken ct);
}

public sealed class GuardDutyQueueWorker(IGuardDutyQueue queue, IServiceScopeFactory scopes, ILogger<GuardDutyQueueWorker> logger) : BackgroundService
{
    public async Task RunOnceAsync(CancellationToken ct)
    {
        foreach (var message in await queue.ReceiveAsync(ct))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                using var processingTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                processingTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                var disposition = await scope.ServiceProvider.GetRequiredService<IGuardDutyEventHandler>().ProcessAsync(message.Body, processingTimeout.Token);
                if (disposition is ScanEventDisposition.Processed or ScanEventDisposition.Ignored)
                    await queue.DeleteAsync(message.ReceiptHandle, ct);
                else logger.LogWarning("GuardDuty message retained for retry/DLQ. Disposition: {Disposition}", disposition);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning("GuardDuty message retained after processing failure. ErrorType: {ErrorType}", ex.GetType().Name);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("GuardDuty receive failed. ErrorType: {ErrorType}", ex.GetType().Name);
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}
