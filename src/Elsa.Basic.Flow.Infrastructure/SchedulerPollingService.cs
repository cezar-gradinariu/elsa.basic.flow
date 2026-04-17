using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Exceptions;
using Elsa.Workflows.Runtime.Requests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Elsa.Basic.Flow.Infrastructure;

/// <summary>
/// Polls MongoDB for scheduled workflow tasks that are due and dispatches them.
///
/// Sleep strategy (Option B — next-task-aware):
///   1. Drain all currently overdue tasks.
///   2. Query the next scheduled task's ResumeAt.
///   3. Sleep until that time, capped at <see cref="MaxSleep"/>.
///   4. Wake early if MongoWorkflowScheduler notifies via SchedulerWakeSignal
///      (a new task was just scheduled with an earlier due time).
///
/// This means the poller only polls when there is actually work to do, while still
/// recovering instantly from crashes (startup immediately drains any overdue tasks).
/// </summary>
public class SchedulerPollingService(
    IMongoDatabase db,
    SchedulerWakeSignal wakeSignal,
    IServiceScopeFactory scopeFactory,
    ILogger<SchedulerPollingService> logger) : BackgroundService
{
    private static readonly TimeSpan MaxSleep = TimeSpan.FromMinutes(5);

    private IMongoCollection<ScheduledTaskDocument> Col =>
        db.GetCollection<ScheduledTaskDocument>("elsa_scheduled_tasks");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[SchedulerPoller] Started — next-task-aware sleep mode (max {Max})", MaxSleep);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);

                var sleep = await NextWakeDelayAsync(stoppingToken);
                if (sleep > TimeSpan.Zero)
                {
                    logger.LogInformation("[SchedulerPoller] Sleeping {Sleep} until next task (or until a new task is scheduled)", sleep);
                    await wakeSignal.WaitAsync(sleep, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[SchedulerPoller] Error during poll — backing off 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        logger.LogInformation("[SchedulerPoller] Stopped");
    }

    private async Task PollAsync(CancellationToken ct)
    {
        var now    = DateTime.UtcNow;
        var filter = Builders<ScheduledTaskDocument>.Filter.Lte(d => d.ResumeAt, now);

        ScheduledTaskDocument? task;
        while ((task = await Col.FindOneAndDeleteAsync(filter, cancellationToken: ct)) is not null)
        {
            using var scope   = scopeFactory.CreateScope();
            var       runtime = scope.ServiceProvider.GetRequiredService<IWorkflowRuntime>();

            try
            {
                await runtime.ExportWorkflowStateAsync(task.WorkflowInstanceId, ct);
            }
            catch (WorkflowInstanceNotFoundException)
            {
                logger.LogWarning(
                    "[SchedulerPoller] Skipping task '{TaskName}' — workflow instance {InstanceId} no longer exists (stale schedule from previous run)",
                    task.TaskName, task.WorkflowInstanceId);
                continue;
            }

            logger.LogInformation(
                "[SchedulerPoller] Dispatching task '{TaskName}' for instance {InstanceId} (was due {ResumeAt:O})",
                task.TaskName, task.WorkflowInstanceId, task.ResumeAt);

            var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();
            await dispatcher.DispatchAsync(
                new DispatchWorkflowInstanceRequest
                {
                    InstanceId = task.WorkflowInstanceId,
                    BookmarkId = task.BookmarkId
                },
                ct);
        }
    }

    private async Task<TimeSpan> NextWakeDelayAsync(CancellationToken ct)
    {
        var next = await Col
            .Find(Builders<ScheduledTaskDocument>.Filter.Empty)
            .Sort(Builders<ScheduledTaskDocument>.Sort.Ascending(d => d.ResumeAt))
            .Limit(1)
            .FirstOrDefaultAsync(ct);

        if (next is null)
            return MaxSleep;

        var delay = next.ResumeAt - DateTime.UtcNow;
        if (delay <= TimeSpan.Zero) return TimeSpan.Zero;
        return delay < MaxSleep ? delay : MaxSleep;
    }
}
