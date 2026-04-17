using Elsa.Basic.Flow.Services.Preparation;
using Elsa.Workflows;
using Microsoft.Extensions.Logging;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Stimuli;

namespace Elsa.Basic.Flow.Services.Fulfilment;

/// <summary>
/// Creates one bookmark per preparation (event name = "preparation-completed-{id}").
/// Each bookmark fires independently when its preparation calls back; a counter stored in
/// WorkflowExecutionContext.Properties (persisted with the workflow instance) tracks how many
/// have completed. The activity completes itself when the last one fires.
///
/// This replaces the external MongoPreparationCompletionTracker — all fan-in state lives
/// inside Elsa's own workflow instance persistence.
/// </summary>
internal class WaitForPreparationsActivity : Activity
{
    private const string TotalKey     = "_prep_total";
    private const string CompletedKey = "_prep_completed";

    public Input<List<PrepareCommand>>? PrepareCommands { get; set; }

    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var commands = context.Get(PrepareCommands)!;
        var props    = context.WorkflowExecutionContext.Properties;

        props[TotalKey]     = commands.Count;
        props[CompletedKey] = 0;

        context.GetRequiredService<ILogger<WaitForPreparationsActivity>>()
            .LogInformation("[FulfilmentWorkflow] Registered {Count} preparation(s), suspending until all complete", commands.Count);

        foreach (var cmd in commands)
        {
            context.CreateBookmark(new CreateBookmarkArgs
            {
                BookmarkName = RuntimeStimulusNames.Event,
                Stimulus     = new EventStimulus($"preparation-completed-{cmd.Id}"),
                Callback     = OnPreparationCompletedAsync,
                AutoBurn     = true
            });
        }

        return ValueTask.CompletedTask;
    }

    private async ValueTask OnPreparationCompletedAsync(ActivityExecutionContext context)
    {
        var props     = context.WorkflowExecutionContext.Properties;
        var total     = Convert.ToInt32(props[TotalKey]);
        var completed = Convert.ToInt32(props[CompletedKey]) + 1;
        props[CompletedKey] = completed;

        var logger = context.GetRequiredService<ILogger<WaitForPreparationsActivity>>();
        logger.LogInformation("[FulfilmentWorkflow] Preparation completed ({Completed}/{Total})", completed, total);

        if (completed >= total)
        {
            logger.LogInformation("[FulfilmentWorkflow] All preparations completed — continuing workflow");
            await context.CompleteActivityAsync();
        }
    }
}
