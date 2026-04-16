using Elsa.Scheduling;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Elsa.Basic.Flow.Infrastructure;

/// <summary>
/// MongoDB-backed implementation of <see cref="IWorkflowScheduler"/>.
/// Persists scheduled tasks so they survive process restarts.
/// On restart, <see cref="SchedulerPollingService"/> picks up any overdue tasks
/// and dispatches them immediately.
/// </summary>
public class MongoWorkflowScheduler(IMongoDatabase db) : IWorkflowScheduler
{
    private IMongoCollection<ScheduledTaskDocument> Col =>
        db.GetCollection<ScheduledTaskDocument>("elsa_scheduled_tasks");

    // ── ScheduleAt (existing instance) — the only path used by the Delay activity ──

    public async ValueTask ScheduleAtAsync(
        string                                  taskName,
        ScheduleExistingWorkflowInstanceRequest request,
        DateTimeOffset                          at,
        CancellationToken                       cancellationToken = default)
    {
        var doc = new ScheduledTaskDocument
        {
            TaskName           = taskName,
            WorkflowInstanceId = request.WorkflowInstanceId,
            BookmarkId         = request.BookmarkId,
            ResumeAt           = at.UtcDateTime
        };

        await Col.ReplaceOneAsync(
            d => d.TaskName == taskName,
            doc,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);

        Console.WriteLine($"[MongoScheduler] Scheduled '{taskName}' at {at:O}  instance={request.WorkflowInstanceId}");
    }

    public async ValueTask UnscheduleAsync(string taskName, CancellationToken cancellationToken = default)
    {
        await Col.DeleteOneAsync(d => d.TaskName == taskName, cancellationToken);
        Console.WriteLine($"[MongoScheduler] Unscheduled '{taskName}'");
    }

    // ── Unsupported overloads — not needed for embedded single-tenant Delay use case ──

    public ValueTask ScheduleAtAsync(string taskName, ScheduleNewWorkflowInstanceRequest request, DateTimeOffset at, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"{nameof(MongoWorkflowScheduler)} does not support scheduling new workflow instances.");

    public ValueTask ScheduleRecurringAsync(string taskName, ScheduleNewWorkflowInstanceRequest request, DateTimeOffset startAt, TimeSpan interval, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"{nameof(MongoWorkflowScheduler)} does not support recurring schedules.");

    public ValueTask ScheduleRecurringAsync(string taskName, ScheduleExistingWorkflowInstanceRequest request, DateTimeOffset startAt, TimeSpan interval, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"{nameof(MongoWorkflowScheduler)} does not support recurring schedules.");

    public ValueTask ScheduleCronAsync(string taskName, ScheduleNewWorkflowInstanceRequest request, string cronExpression, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"{nameof(MongoWorkflowScheduler)} does not support cron schedules.");

    public ValueTask ScheduleCronAsync(string taskName, ScheduleExistingWorkflowInstanceRequest request, string cronExpression, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"{nameof(MongoWorkflowScheduler)} does not support cron schedules.");
}

[BsonIgnoreExtraElements]
internal sealed class ScheduledTaskDocument
{
    [BsonId]
    public string    TaskName           { get; set; } = "";
    public string    WorkflowInstanceId { get; set; } = "";
    public string?   BookmarkId         { get; set; }
    public DateTime  ResumeAt           { get; set; }
}
