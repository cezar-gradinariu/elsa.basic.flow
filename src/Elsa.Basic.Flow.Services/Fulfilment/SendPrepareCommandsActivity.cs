using System.Text.Json;
using Elsa.Basic.Flow.Services.Allocation;
using Microsoft.Extensions.Logging;
using Elsa.Basic.Flow.Services.Preparation;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Messages;
using Elsa.Workflows.Runtime.Requests;

namespace Elsa.Basic.Flow.Services.Fulfilment;

internal class SendPrepareCommandsActivity : Activity
{
    public Input<AllocationResult>?      AllocationResult { get; set; }
    public Input<string>?                FulfilmentId     { get; set; }
    public Output<List<PrepareCommand>>? PrepareCommands  { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var result             = context.Get(AllocationResult)!;
        var fulfilmentId       = context.Get(FulfilmentId) ?? "unknown";
        var parentInstanceId   = context.WorkflowExecutionContext.Id;
        var logger             = context.GetRequiredService<ILogger<SendPrepareCommandsActivity>>();

        var commands = result.StoreAllocations
            .Select(a => new PrepareCommand(Guid.NewGuid(), a.StoreId, a.Lines, fulfilmentId, parentInstanceId))
            .ToList();

        logger.LogInformation("[FulfilmentWorkflow] Dispatching {Count} preparation(s) for order {OrderNo}", commands.Count, result.OrderNo);
        foreach (var cmd in commands)
            logger.LogInformation("  → PrepareCommand id={Id}  storeId={StoreId}  lines={Lines}", cmd.Id, cmd.StoreId, cmd.Lines.Count);

        context.Set(PrepareCommands, commands);

        var runtime    = context.GetRequiredService<IWorkflowRuntime>();
        var dispatcher = context.GetRequiredService<IWorkflowDispatcher>();

        foreach (var cmd in commands)
        {
            var client = await runtime.CreateClientAsync(cancellationToken: context.CancellationToken);
            await client.CreateInstanceAsync(new CreateWorkflowInstanceRequest
            {
                WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(nameof(PreparationWorkflow)),
                CorrelationId            = $"preparation-{cmd.Id}",
                Input = new Dictionary<string, object>
                {
                    ["PrepareCommandId"]        = cmd.Id.ToString(),
                    ["StoreId"]                 = cmd.StoreId,
                    ["Lines"]                   = JsonSerializer.Serialize(cmd.Lines),
                    ["FulfilmentId"]            = cmd.FulfilmentId,
                    ["ParentWorkflowInstanceId"] = cmd.ParentWorkflowInstanceId
                }
            }, context.CancellationToken);

            await dispatcher.DispatchAsync(
                new DispatchWorkflowInstanceRequest { InstanceId = client.WorkflowInstanceId },
                context.CancellationToken);

            logger.LogInformation("  ✓ PrepareCommand id={Id} dispatched as workflow {WorkflowInstanceId}", cmd.Id, client.WorkflowInstanceId);
        }

        await context.CompleteActivityAsync();
    }
}
