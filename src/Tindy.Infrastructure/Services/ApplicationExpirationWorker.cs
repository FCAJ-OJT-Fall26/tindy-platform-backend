using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tindy.Application.Interfaces.Repositories;

namespace Tindy.Infrastructure.Services;

public sealed class ApplicationExpirationWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<ApplicationExpirationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<IProjectApplicationRepository>();
                var now = clock.GetUtcNow().UtcDateTime;
                var count = await repository.ExpireOverdueApplicationsAsync(now, stoppingToken);
                if (count > 0)
                {
                    logger.LogInformation("Expired {Count} overdue project applications.", count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Application expiration worker cycle failed. ErrorType: {ErrorType}, Message: {Message}", ex.GetType().Name, ex.Message);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
