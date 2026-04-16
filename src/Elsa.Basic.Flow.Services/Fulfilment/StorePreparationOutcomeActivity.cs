using Elsa.Workflows;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Services.Fulfilment;

internal class StorePreparationOutcomeActivity : Activity
{
    public Output<string>? FulfilmentIdOut { get; set; }
    public Output<string>? PrepareCommandIdOut { get; set; }
    public Output<string>? PreparationOutcomeOut { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            var input = context.WorkflowExecutionContext.Input;

            // Extract data from workflow input (sent by API when triggering)
            object? fulfilmentIdVal = null, prepareCommandIdVal = null, outcomeVal = null;
            input?.TryGetValue("FulfilmentId", out fulfilmentIdVal);
            input?.TryGetValue("PrepareCommandId", out prepareCommandIdVal);
            input?.TryGetValue("PreparationOutcome", out outcomeVal);

            var fulfilmentId = fulfilmentIdVal?.ToString() ?? "";
            var prepareCommandId = prepareCommandIdVal?.ToString() ?? "";
            var outcome = outcomeVal?.ToString() ?? "";

            Console.WriteLine($"[StorePreparationOutcome] Storing outcome data:");
            Console.WriteLine($"  FulfilmentId: {fulfilmentId}");
            Console.WriteLine($"  PrepareCommandId: {prepareCommandId}");
            Console.WriteLine($"  Outcome length: {outcome.Length} chars");

            // Store in workflow variables for next activity
            context.Set(FulfilmentIdOut, fulfilmentId);
            context.Set(PrepareCommandIdOut, prepareCommandId);
            context.Set(PreparationOutcomeOut, outcome);

            Console.WriteLine($"[StorePreparationOutcome] ✓ Data stored in workflow variables");

            await context.CompleteActivityAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StorePreparationOutcome] ✗ Error storing outcome data: {ex.Message}");
            throw;
        }
    }
}