using Elsa.Basic.Flow.Services.Preparation;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Stimuli;

namespace Elsa.Basic.Flow.Services.Fulfilment;

/// <summary>
/// Atomically registers the expected preparations with the completion tracker and creates
/// the Event bookmark that the /api/preparation-outcome endpoint will signal when all are done.
/// Combining both steps in one ExecuteAsync eliminates the race condition that would exist if
/// tracker registration and bookmark creation were separate activities.
/// </summary>
internal class WaitForPreparationsActivity : Activity
{
    private const string EventName = "preparations-all-completed";

    public Input<List<PrepareCommand>>? PrepareCommands { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var commands           = context.Get(PrepareCommands)!;
        var workflowInstanceId = context.WorkflowExecutionContext.Id;
        var tracker            = context.GetRequiredService<IPreparationCompletionTracker>();
        var preparationIds     = commands.Select(c => c.Id.ToString()).ToList();

        // Register first — then create the bookmark in the same execution step so that
        // both changes are persisted atomically by Elsa before any preparation can signal back.
        await tracker.RegisterPreparations(workflowInstanceId, preparationIds);

        Console.WriteLine($"[FulfilmentWorkflow] Registered {preparationIds.Count} preparation(s), suspending until all complete...");

        // Create a bookmark that is hash-compatible with Elsa's Event activity so the
        // existing IStimulusSender call in /api/preparation-outcome resumes this activity.
        context.CreateBookmark(new CreateBookmarkArgs
        {
            BookmarkName = RuntimeStimulusNames.Event,
            Stimulus     = new EventStimulus(EventName),
            Callback     = OnPreparationsCompletedAsync,
            AutoBurn     = true
        });
        // No CompleteActivityAsync — the activity stays suspended until the bookmark is resumed.
    }

    private async ValueTask OnPreparationsCompletedAsync(ActivityExecutionContext context)
    {
        Console.WriteLine("[FulfilmentWorkflow] All preparations completed signal received — continuing workflow.");
        await context.CompleteActivityAsync();
    }
}
