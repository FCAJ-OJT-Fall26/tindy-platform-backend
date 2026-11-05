using Microsoft.EntityFrameworkCore;
using Tindy.Application.Interfaces.Services;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Api.Hubs;

public sealed class ChatRequestWorker(IServiceScopeFactory scopes, ILogger<ChatRequestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IChatRequests>().SweepAsync(stoppingToken);
                var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
                var users = await db.Database.SqlQueryRaw<Guid>("""
                    WITH removed AS (
                        DELETE FROM chat_request_events WHERE event_id IN (
                            SELECT event_id FROM chat_request_events ORDER BY event_id FOR UPDATE SKIP LOCKED LIMIT 100
                        ) RETURNING user_ids
                    ) SELECT DISTINCT unnest(user_ids) AS "Value" FROM removed
                    """).ToArrayAsync(stoppingToken);
                if (users.Length > 0) await scope.ServiceProvider.GetRequiredService<IChatNotifier>().RequestsChangedAsync(users, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Chat lifecycle retry required: {Type}", ex.GetType().Name); }
        }
    }
}
