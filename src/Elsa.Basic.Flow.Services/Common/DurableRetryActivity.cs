using Elsa.Scheduling;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Microsoft.Extensions.Logging;

namespace Elsa.Basic.Flow.Services.Common;

internal abstract class DurableRetryActivity : Activity
{
    protected virtual int MaxAttempts => 4;

    protected abstract ValueTask<bool> TryExecuteAsync(ActivityExecutionContext context);

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
        => await AttemptAsync(context);

    private async ValueTask AttemptAsync(ActivityExecutionContext context)
    {
        var props    = context.WorkflowExecutionContext.Properties;
        var countKey = $"_retry_{context.Activity.Id}";
        var count    = props.TryGetValue(countKey, out var r) ? Convert.ToInt32(r) : 0;

        var logger = context.GetRequiredService<ILoggerFactory>().CreateLogger(GetType().FullName!);
        logger.LogInformation("[{Activity}] attempt {Attempt}/{Max}", GetType().Name, count + 1, MaxAttempts);

        var succeeded = await TryExecuteAsync(context);

        if (succeeded)
        {
            await context.CompleteActivityAsync();
            return;
        }

        count++;

        if (count >= MaxAttempts)
        {
            logger.LogError("[{Activity}] failed after {Max} attempts — aborting", GetType().Name, MaxAttempts);
            throw new InvalidOperationException($"{GetType().Name} failed after {MaxAttempts} attempts");
        }

        props[countKey] = count;

        var delay    = TimeSpan.FromSeconds(Math.Pow(2, count));
        var resumeAt = DateTimeOffset.UtcNow.Add(delay);

        logger.LogWarning("[{Activity}] retry {Count}/{Max} — resuming in {Delay}s",
            GetType().Name, count, MaxAttempts, delay.TotalSeconds);

        var bookmark  = context.CreateBookmark(new CreateBookmarkArgs { Callback = AttemptAsync, AutoBurn = true });
        var scheduler = context.GetRequiredService<IWorkflowScheduler>();
        await scheduler.ScheduleAtAsync(
            $"retry:{context.WorkflowExecutionContext.Id}:{context.Activity.Id}",
            new ScheduleExistingWorkflowInstanceRequest
            {
                WorkflowInstanceId = context.WorkflowExecutionContext.Id,
                BookmarkId         = bookmark.Id
            },
            resumeAt,
            context.CancellationToken);
    }
}
