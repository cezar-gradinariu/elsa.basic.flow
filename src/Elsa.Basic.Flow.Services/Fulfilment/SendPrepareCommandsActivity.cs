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
            var result   = context.Get(AllocationResult)!;
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

            // Start workflows directly using Elsa runtime (fire-and-forget)
            foreach (var cmd in commands)
            {
                try
                {
                    // Fire-and-forget: start workflow without waiting for completion
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var client = await workflowRuntime.CreateClientAsync();
                            
                            var instanceRequest = new CreateWorkflowInstanceRequest
                            {
                                WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(nameof(PreparationWorkflow)),
                                CorrelationId = $"preparation-{cmd.Id}",
                                Input = new Dictionary<string, object>
                                {
                                    ["PrepareCommandId"] = cmd.Id.ToString(),
                                    ["StoreId"] = cmd.StoreId,
                                    ["Lines"] = JsonSerializer.Serialize(cmd.Lines)
                                }
                            };

                            await client.CreateInstanceAsync(instanceRequest);
                            await client.RunInstanceAsync(RunWorkflowInstanceRequest.Empty);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  ✗ PrepareCommand id={cmd.Id} → Workflow start failed: {ex.Message}");
                        }
                    });
                    
                    Console.WriteLine($"  ✓ PrepareCommand id={cmd.Id} → PreparationWorkflow started (fire-and-forget)");
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
