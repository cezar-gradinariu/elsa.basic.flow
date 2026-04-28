using Elsa.Workflows;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Stimuli;
using Microsoft.Extensions.Logging;

namespace Elsa.Basic.Flow.Services.Preparation.Activities;

/// <summary>
/// Runs inside the TryCatch catch block in StorePreparationSubWorkflow.
/// Sends store-prep-failed-{prepCommandId} so the parent FulfilmentWorkflow
/// can fault instead of hanging on a bookmark that will never fire.
/// </summary>
internal class NotifyParentOfFaultActivity : Activity
{
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var props = context.WorkflowExecutionContext.Properties;
        props.TryGetValue(InitialiseStorePreparationPropertiesActivity.PrepCommandIdKey, out var pidRaw);
        var prepCommandId = pidRaw?.ToString() ?? Guid.Empty.ToString();

        var logger = context.GetRequiredService<ILogger<NotifyParentOfFaultActivity>>();
        logger.LogError("[StorePreparation] prep={PrepId} failed permanently — signalling parent to fault", prepCommandId);

        var stimulusSender = context.GetRequiredService<IStimulusSender>();
        await stimulusSender.SendAsync(
            RuntimeStimulusNames.Event,
            new EventStimulus($"store-prep-failed-{prepCommandId}"),
            new StimulusMetadata(),
            context.CancellationToken);

        await context.CompleteActivityAsync();
    }
}
