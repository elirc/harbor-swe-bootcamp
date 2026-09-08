using Harbor.Infrastructure;

namespace Harbor.Api.Infrastructure;

public sealed class PublicationScheduleWorker(IServiceScopeFactory scopes, IConfiguration config,
    TimeProvider clock, ILogger<PublicationScheduleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue<bool>("Workers:PublicationSchedules:Enabled")) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), clock);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var count = await scope.ServiceProvider.GetRequiredService<PublicationScheduler>().ProcessDueAsync(clock.GetUtcNow(), ct: stoppingToken);
                if (count > 0) logger.LogInformation("Processed {Count} publication schedules", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError("Publication schedule scan failed: {ErrorType}", ex.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
