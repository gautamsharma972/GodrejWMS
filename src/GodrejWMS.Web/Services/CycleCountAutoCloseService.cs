using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.CycleCount;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Web.Services;

/// <summary>Once a minute closes the cycle counts whose Schedule To has passed (for batches scheduled to release the Inward / Pullout freeze at
/// their end), so the batch list shows them Closed and the warehouse is not left frozen by a count nobody closed. The freeze itself already
/// ends at Schedule To for those batches; this only brings the batch status in line.</summary>
public sealed class CycleCountAutoCloseService(
    IApplicationDbContextFactory dbFactory, IRealtimeNotificationService notifications, ILogger<CycleCountAutoCloseService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the application finish starting (and applying migrations) before the first look at the database.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); } catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                try
                {
                    var closed = await CycleCountAutoClose.CloseEndedBatchesAsync(db, DateTimeOffset.UtcNow, stoppingToken);
                    foreach (var batch in closed)
                    {
                        logger.LogInformation("Cycle count {Batch} closed automatically: Schedule To has passed.", batch.BatchNumber);
                        try
                        {
                            await notifications.PublishAsync(new RealtimeNotification(
                                Guid.NewGuid(), "Cycle count closed", $"{batch.BatchNumber} was closed automatically because its schedule ended. Inward and Pullout creation is allowed again for the warehouse unless another cycle count is open.",
                                "info", "/cycle-count/batches", DateTimeOffset.UtcNow,
                                AudienceRoles: ["Superadmin", "Head Supervisor", "Cycle Count Scheduler", "Cycle Count Performer"]), stoppingToken);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            logger.LogWarning(ex, "Could not announce the automatic close of {Batch}", batch.BatchNumber);
                        }
                    }
                }
                finally
                {
                    if (db is IAsyncDisposable disposable) await disposable.DisposeAsync();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another instance closed the same batch a moment earlier; the next tick sees the result.
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Automatic close of ended cycle counts failed; it will be tried again.");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try { return await timer.WaitForNextTickAsync(token); }
        catch (OperationCanceledException) { return false; }
    }
}
