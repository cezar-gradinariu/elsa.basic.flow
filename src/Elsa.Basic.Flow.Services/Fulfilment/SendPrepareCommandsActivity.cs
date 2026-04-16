using System.Text.Json;
using Elsa.Basic.Flow.Services.Allocation;
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
    public Output<List<PrepareCommand>>? PrepareCommands  { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var result = context.Get(AllocationResult)!;

        var input        = context.WorkflowExecutionContext.Input;
        var fulfilmentId = input?.TryGetValue("FulfilmentId", out var fv) == true
            ? fv?.ToString() ?? "unknown"
            : "unknown";

        var commands = result.StoreAllocations
            .Select(a => new PrepareCommand(Guid.NewGuid(), a.StoreId, a.Lines, fulfilmentId))
            .ToList();

        Console.WriteLine();
        Console.WriteLine($"[FulfilmentWorkflow] Dispatching {commands.Count} preparation(s) for order {result.OrderNo}:");
        foreach (var cmd in commands)
        {
            Console.WriteLine($"  → PrepareCommand id={cmd.Id}  storeId={cmd.StoreId}  lines={cmd.Lines.Count}");
            foreach (var line in cmd.Lines)
                Console.WriteLine($"      {line.Sku,-20} qty:{line.Quantity,3}  {line.UnitOfMeasure}");
        }

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
                    ["PrepareCommandId"] = cmd.Id.ToString(),
                    ["StoreId"]          = cmd.StoreId,
                    ["Lines"]            = JsonSerializer.Serialize(cmd.Lines),
                    ["FulfilmentId"]     = cmd.FulfilmentId
                }
            }, context.CancellationToken);

            await dispatcher.DispatchAsync(
                new DispatchWorkflowInstanceRequest { InstanceId = client.WorkflowInstanceId },
                context.CancellationToken);

            Console.WriteLine($"  ✓ PrepareCommand id={cmd.Id} dispatched as workflow {client.WorkflowInstanceId}");
        }

        await context.CompleteActivityAsync();
    }
}
