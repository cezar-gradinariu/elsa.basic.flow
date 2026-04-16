using Elsa.Workflows;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Services.Fulfilment;

internal class CompleteFulfilmentActivity : Activity
{
    public Input<string>? FulfilmentId { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var fulfilmentId = context.Get(FulfilmentId) ?? "unknown";

        // Reaching this activity means the WaitForPreparationsActivity bookmark was
        // triggered, which only happens after all preparations have signalled completion.
        Console.WriteLine($"[FulfilmentWorkflow] Fulfilment {fulfilmentId} completed successfully.");
        Console.WriteLine($"[FulfilmentWorkflow] All preparation workflows have finished and returned their outcomes.");

        await context.CompleteActivityAsync();
    }
}
