using Elsa.Basic.Flow.Services.Preparation;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Basic.Flow.Services.Fulfilment;

internal class WaitForPreparationsActivity : Activity
{
    public Input<List<PrepareCommand>>? PrepareCommands { get; set; }
    public Output<int>? CompletedCount { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            var commands = context.Get(PrepareCommands)!;
            var workflowInstanceId = context.WorkflowExecutionContext.Id;
            
            Console.WriteLine($"[FulfilmentWorkflow] Waiting for {commands.Count} preparation(s) to complete...");
            
            // Register preparations with the completion tracker
            var tracker = context.WorkflowExecutionContext.ServiceProvider.GetRequiredService<IPreparationCompletionTracker>();
            var preparationIds = commands.Select(c => c.Id.ToString()).ToList();
            
            await tracker.RegisterPreparations(workflowInstanceId, preparationIds);
            
            // Set initial completed count to 0
            context.Set(CompletedCount, 0);
            
            Console.WriteLine($"  → Registered {preparationIds.Count} preparations for tracking");
            Console.WriteLine($"  → Tracking preparations: {string.Join(", ", preparationIds)}");
            Console.WriteLine($"[FulfilmentWorkflow] Initial setup complete - workflow will continue to wait for events...");

            await context.CompleteActivityAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FulfilmentWorkflow] Error in WaitForPreparations: {ex.Message}");
            throw;
        }
    }
}