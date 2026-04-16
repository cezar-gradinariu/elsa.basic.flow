using Elsa.Workflows;

namespace Elsa.Basic.Flow.Services.Fulfilment;

internal class CompleteFulfilmentActivity : Activity
{
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var input        = context.WorkflowExecutionContext.Input;
        var fulfilmentId = input?.TryGetValue("FulfilmentId", out var fv) == true
            ? fv?.ToString() ?? "unknown"
            : "unknown";

        // Reaching this activity means the WaitForPreparationsActivity bookmark was
        // triggered, which only happens after all preparations have signalled completion.
        Console.WriteLine($"[FulfilmentWorkflow] Fulfilment {fulfilmentId} completed successfully.");
        Console.WriteLine($"[FulfilmentWorkflow] All preparation workflows have finished and returned their outcomes.");

        await context.CompleteActivityAsync();
    }
}
