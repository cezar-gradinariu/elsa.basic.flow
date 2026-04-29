using System.Text.Json;
using Elsa.Basic.Flow.Domain;
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
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var props  = context.WorkflowExecutionContext.Properties;
        var logger = context.GetRequiredService<ILogger<DispatchAndWaitPreparationsActivity>>();

        var total = props.TryGetValue(InitialisePreparationPlanActivity.TotalKey, out var t)
            ? Convert.ToInt32(t) : 0;

        if (total == 0)
        {
            logger.LogWarning("[FulfilmentWorkflow] No store preparations planned — skipping");
            await context.CompleteActivityAsync();
            return;
        }

        props.TryGetValue(InitialisePreparationPlanActivity.PlanKey,       out var planRaw);
        props.TryGetValue(InitialisePreparationPlanActivity.FulfilmentKey,  out var fidRaw);

        var plan         = JsonSerializer.Deserialize<List<PreparationPlanItem>>(planRaw?.ToString() ?? "[]") ?? [];
        var fulfilmentId = fidRaw?.ToString() ?? string.Empty;

        logger.LogInformation("[FulfilmentWorkflow] Dispatching {Count} store preparation subworkflow(s)", plan.Count);

        var runtime    = context.GetRequiredService<IWorkflowRuntime>();
        var dispatcher = context.GetRequiredService<IWorkflowDispatcher>();

        foreach (var item in plan)
        {
            var client = await runtime.CreateClientAsync(cancellationToken: context.CancellationToken);
            await client.CreateInstanceAsync(new CreateWorkflowInstanceRequest
            {
                WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(nameof(StorePreparationSubWorkflow)),
                CorrelationId            = $"store-prep-{item.PrepCommandId}",
                Input = new Dictionary<string, object>
                {
                    ["PrepCommandId"] = item.PrepCommandId.ToString(),
                    ["FulfilmentId"]  = fulfilmentId,
                    ["StoreId"]       = item.StoreId,
                    ["Lines"]         = JsonSerializer.Serialize(item.Lines)
                }
            }, context.CancellationToken);

            await dispatcher.DispatchAsync(
                new DispatchWorkflowInstanceRequest { InstanceId = client.WorkflowInstanceId },
                context.CancellationToken);

            context.CreateBookmark(new CreateBookmarkArgs
            {
                BookmarkName = RuntimeStimulusNames.Event,
                Stimulus     = new EventStimulus($"store-prep-completed-{item.PrepCommandId}"),
                Callback     = OnStorePrepSignalReceivedAsync,
                AutoBurn     = true
            });

            // Fault bookmark — fired by NotifyParentOfFaultActivity if the subworkflow fails permanently.
            context.CreateBookmark(new CreateBookmarkArgs
            {
                BookmarkName = RuntimeStimulusNames.Event,
                Stimulus     = new EventStimulus($"store-prep-failed-{item.PrepCommandId}"),
                Callback     = OnStorePrepFailedAsync,
                AutoBurn     = true
            });

            logger.LogInformation("[FulfilmentWorkflow] Subworkflow dispatched — store={StoreId}  prep={PrepId}",
                item.StoreId, item.PrepCommandId);
        }
    }

    private async ValueTask OnStorePrepSignalReceivedAsync(ActivityExecutionContext context)
    {
        var props = context.WorkflowExecutionContext.Properties;
        var total = props.TryGetValue(InitialisePreparationPlanActivity.TotalKey,      out var t) ? Convert.ToInt32(t) : 0;
        var fid   = props.TryGetValue(InitialisePreparationPlanActivity.FulfilmentKey, out var f) ? f?.ToString() ?? string.Empty : string.Empty;

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
