using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tindy.Infrastructure.Media;

public sealed class MediaLifecycleWorker(IServiceScopeFactory scopeFactory, IOptions<MediaLifecycleOptions> options,
    ILogger<MediaLifecycleWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(RunLoopAsync(scan: true, stoppingToken), RunLoopAsync(scan: false, stoppingToken));

    private async Task RunLoopAsync(bool scan, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var lifecycle = scope.ServiceProvider.GetRequiredService<MediaLifecycleService>();
                if (scan) await lifecycle.RunScanCycleAsync(stoppingToken);
                else
                {
                    await lifecycle.RunCleanupCycleAsync(stoppingToken);
                    await scope.ServiceProvider.GetRequiredService<MediaOrphanReconciler>().RunIfDueAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Media worker cycle failed. ErrorType: {ErrorType}", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
