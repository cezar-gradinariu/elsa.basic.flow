using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Basic.Flow.Services.Allocation;
using Elsa.Basic.Flow.Services.Fulfilment.Workflows;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Messages;
using Elsa.Workflows.Runtime.Requests;
using Elsa.Workflows.Runtime.Stimuli;
using Microsoft.Extensions.Logging;

namespace Elsa.Basic.Flow.Services.Fulfilment.Activities;

internal class DispatchAndWaitPreparationsActivity : Activity
{
    private const string TotalKey      = "_dpwa_total";
    private const string FulfilmentKey = "_dpwa_fulfilment_id";

    public Input<AllocationResult>? AllocationResult { get; set; }
    public Input<string>?           FulfilmentId     { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var result       = context.Get(AllocationResult)!;
        var fulfilmentId = context.Get(FulfilmentId) ?? string.Empty;
        var props        = context.WorkflowExecutionContext.Properties;
        var logger       = context.GetRequiredService<ILogger<DispatchAndWaitPreparationsActivity>>();

        if (result.StoreAllocations.Count == 0)
        {
            logger.LogWarning("[FulfilmentWorkflow] No store allocations — skipping preparations");
            await context.CompleteActivityAsync();
            return;
        }

        props[TotalKey]      = result.StoreAllocations.Count;
        props[FulfilmentKey] = fulfilmentId;

        logger.LogInformation("[FulfilmentWorkflow] Dispatching {Count} store preparation subworkflow(s)", result.StoreAllocations.Count);

        var runtime    = context.GetRequiredService<IWorkflowRuntime>();
        var dispatcher = context.GetRequiredService<IWorkflowDispatcher>();

        foreach (var allocation in result.StoreAllocations)
        {
            var prepCommandId = Guid.NewGuid();

            var client = await runtime.CreateClientAsync(cancellationToken: context.CancellationToken);
            await client.CreateInstanceAsync(new CreateWorkflowInstanceRequest
            {
                WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(nameof(StorePreparationSubWorkflow)),
                CorrelationId            = $"store-prep-{prepCommandId}",
                Input = new Dictionary<string, object>
                {
                    ["PrepCommandId"] = prepCommandId.ToString(),
                    ["FulfilmentId"]  = fulfilmentId,
                    ["StoreId"]       = allocation.StoreId,
                    ["Lines"]         = JsonSerializer.Serialize(allocation.Lines)
                }
            }, context.CancellationToken);

            await dispatcher.DispatchAsync(
                new DispatchWorkflowInstanceRequest { InstanceId = client.WorkflowInstanceId },
                context.CancellationToken);

            context.CreateBookmark(new CreateBookmarkArgs
            {
                BookmarkName = RuntimeStimulusNames.Event,
                Stimulus     = new EventStimulus($"store-prep-completed-{prepCommandId}"),
                Callback     = OnStorePrepSignalReceivedAsync,
                AutoBurn     = true
            });

            // Fault bookmark — fired by NotifyParentOfFaultActivity if the subworkflow fails.
            // Prevents the parent from hanging on a bookmark that will never fire.
            context.CreateBookmark(new CreateBookmarkArgs
            {
                BookmarkName = RuntimeStimulusNames.Event,
                Stimulus     = new EventStimulus($"store-prep-failed-{prepCommandId}"),
                Callback     = OnStorePrepFailedAsync,
                AutoBurn     = true
            });

            logger.LogInformation("[FulfilmentWorkflow] Subworkflow dispatched — store={StoreId}  prep={PrepId}", allocation.StoreId, prepCommandId);
        }
    }

    private async ValueTask OnStorePrepSignalReceivedAsync(ActivityExecutionContext context)
    {
        var props = context.WorkflowExecutionContext.Properties;
        var total = props.TryGetValue(TotalKey,      out var t) ? Convert.ToInt32(t) : 0;
        var fid   = props.TryGetValue(FulfilmentKey, out var f) ? f?.ToString() ?? string.Empty : string.Empty;

        var logger = context.GetRequiredService<ILogger<DispatchAndWaitPreparationsActivity>>();

        // $inc is atomic under concurrent stimulus deliveries for the same workflow instance.
        int completed = 0;
        if (Guid.TryParse(fid, out var fulfilmentGuid))
        {
            var repository = context.GetRequiredService<IFulfilmentRepository>();
            completed = await repository.IncrementPrepCompletedAsync(fulfilmentGuid, context.CancellationToken);
        }

        logger.LogInformation("[FulfilmentWorkflow] Store preparation completed ({Completed}/{Total})", completed, total);

        if (completed >= total)
        {
            logger.LogInformation("[FulfilmentWorkflow] All store preparations done — continuing");
            await context.CompleteActivityAsync();
        }
    }

    private ValueTask OnStorePrepFailedAsync(ActivityExecutionContext context)
    {
        var logger = context.GetRequiredService<ILogger<DispatchAndWaitPreparationsActivity>>();
        logger.LogError("[FulfilmentWorkflow] A store preparation subworkflow failed — faulting fulfilment workflow");
        throw new InvalidOperationException(
            "A store preparation subworkflow reported failure. Fulfilment cannot complete. Check subworkflow logs for details.");
    }
}
