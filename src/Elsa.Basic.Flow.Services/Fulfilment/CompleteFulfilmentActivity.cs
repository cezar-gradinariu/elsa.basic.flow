using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Services.Fulfilment;

internal class CompleteFulfilmentActivity : Activity
{
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            // Get information from workflow input about what was completed
            var input = context.WorkflowExecutionContext.Input;
            var allCompleted = input?.TryGetValue("AllPreparationsCompleted", out var completed) == true && 
                              bool.TryParse(completed?.ToString(), out var isCompleted) && isCompleted;

            var fulfilmentId = "unknown-fulfilment-id";
            if (input != null && input.TryGetValue("FulfilmentId", out var fulfilmentIdVal))
            {
                fulfilmentId = fulfilmentIdVal?.ToString() ?? "unknown-fulfilment-id";
            }

            if (allCompleted)
            {
                Console.WriteLine($"[FulfilmentWorkflow] 🎉 Fulfilment {fulfilmentId} completed successfully!");
                Console.WriteLine($"[FulfilmentWorkflow] All preparation workflows have finished and returned their outcomes.");
                Console.WriteLine($"[FulfilmentWorkflow] The order is now ready for final processing and shipment.");
            }
            else
            {
                Console.WriteLine($"[FulfilmentWorkflow] ⚠️  Fulfilment {fulfilmentId} completed but preparation status is unclear");
            }

            await context.CompleteActivityAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FulfilmentWorkflow] Error in CompleteFulfilment: {ex.Message}");
            throw;
        }
    }
}