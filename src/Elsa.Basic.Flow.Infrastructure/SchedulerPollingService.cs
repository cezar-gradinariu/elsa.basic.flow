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
/// Provides restart-recovery: any task persisted before a crash is picked up on the next start.
/// Uses <see cref="IMongoCollection{TDocument}.FindOneAndDeleteAsync"/> to atomically
/// claim-and-remove each task, preventing duplicate dispatches.
/// </summary>
public class SchedulerPollingService(
    IMongoDatabase db,
    IServiceScopeFactory scopeFactory,
    ILogger<SchedulerPollingService> logger) : BackgroundService
{
    private IMongoCollection<ScheduledTaskDocument> Col =>
        db.GetCollection<ScheduledTaskDocument>("elsa_scheduled_tasks");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[SchedulerPoller] Started — polling every second for due scheduled tasks");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[SchedulerPoller] Error during poll");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
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
}
