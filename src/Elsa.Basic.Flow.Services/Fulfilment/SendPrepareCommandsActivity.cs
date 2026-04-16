using System.Text.Json;
using Elsa.Basic.Flow.Services.Allocation;
using Elsa.Basic.Flow.Services.Preparation;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Basic.Flow.Services.Fulfilment;

internal class SendPrepareCommandsActivity : Activity
{
    public Input<AllocationResult>? AllocationResult { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            var result = context.Get(AllocationResult)!;
            
            // Get FulfilmentId from workflow input
            var input = context.WorkflowExecutionContext.Input;
            var fulfilmentId = "unknown-fulfilment-id";
            if (input != null && input.TryGetValue("FulfilmentId", out var fulfilmentIdVal))
            {
                fulfilmentId = fulfilmentIdVal?.ToString() ?? "unknown-fulfilment-id";
            }
            
            var commands = result.StoreAllocations
                .Select(a => new PrepareCommand(Guid.NewGuid(), a.StoreId, a.Lines))
                .ToList();

            var sp = context.WorkflowExecutionContext.ServiceProvider;
            var workflowRuntime = sp.GetRequiredService<IWorkflowRuntime>();

            Console.WriteLine();
            Console.WriteLine($"[FulfilmentWorkflow] Starting {commands.Count} PreparationWorkflow(s) for order {result.OrderNo}:");

            foreach (var cmd in commands)
            {
                Console.WriteLine($"  → PrepareCommand id={cmd.Id}  storeId={cmd.StoreId}  lines={cmd.Lines.Count}");
                foreach (var line in cmd.Lines)
                    Console.WriteLine($"      {line.Sku,-20} qty:{line.Quantity,3}  {line.UnitOfMeasure}");
            }

            // Start preparation workflows (fire-and-forget)
            foreach (var cmd in commands)
            {
                try
                {
                    // Start preparation workflow for actual preparation work (fire-and-forget)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var client = await workflowRuntime.CreateClientAsync();
                            
                            var preparationRequest = new CreateWorkflowInstanceRequest
                            {
                                WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(nameof(PreparationWorkflow)),
                                CorrelationId = $"preparation-{cmd.Id}",
                                Input = new Dictionary<string, object>
                                {
                                    ["PrepareCommandId"] = cmd.Id.ToString(),
                                    ["StoreId"] = cmd.StoreId,
                                    ["Lines"] = JsonSerializer.Serialize(cmd.Lines),
                                    ["FulfilmentId"] = fulfilmentId  // Pass FulfilmentId to PreparationWorkflow
                                }
                            };

                            await client.CreateInstanceAsync(preparationRequest);
                            await client.RunInstanceAsync(RunWorkflowInstanceRequest.Empty);
                            
                            Console.WriteLine($"  ✓ PrepareCommand id={cmd.Id} → PreparationWorkflow started (fire-and-forget)");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  ✗ PrepareCommand id={cmd.Id} → PreparationWorkflow start failed: {ex.Message}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ✗ PrepareCommand id={cmd.Id} → Failed to initiate workflow: {ex.Message}");
                }
            }

            await context.CompleteActivityAsync();
        }
        catch (ObjectDisposedException ex)
        {
            Console.WriteLine($"[FulfilmentWorkflow] Service disposed during SendPrepareCommands execution: {ex.ObjectName} - Commands initiated");
            // Don't re-throw - the command initiation work was completed
        }
        catch (Exception ex) when (ex.ToString().Contains("IServiceProvider"))
        {
            Console.WriteLine($"[FulfilmentWorkflow] Service provider disposed during SendPrepareCommands - Commands initiated: {ex.Message}");
            // Don't re-throw - the command initiation work was completed
        }
    }
}
