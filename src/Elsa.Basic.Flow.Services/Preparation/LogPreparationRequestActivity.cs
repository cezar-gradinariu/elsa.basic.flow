using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Workflows;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Services.Preparation;

internal class LogPreparationRequestActivity : Activity
{
    public int DelaySeconds { get; set; }
    public Output<string>? PrepareCommandIdOut { get; set; }
    public Output<string>? LinesJsonOut { get; set; }
    public Output<string>? FulfilmentIdOut { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            var input = context.WorkflowExecutionContext.Input;

            object? idVal = null, storeIdVal = null, linesVal = null, fulfilmentIdVal = null;
            input?.TryGetValue("PrepareCommandId", out idVal);
            input?.TryGetValue("StoreId",          out storeIdVal);
            input?.TryGetValue("Lines",            out linesVal);
            input?.TryGetValue("FulfilmentId",     out fulfilmentIdVal);

            var id      = idVal?.ToString()     ?? "(unknown)";
            var storeId = storeIdVal?.ToString() ?? "(unknown)";
            var linesJson = linesVal?.ToString() ?? "[]";
            var fulfilmentId = fulfilmentIdVal?.ToString() ?? "(unknown)";
            var lines   = JsonSerializer.Deserialize<List<OrderLine>>(linesJson) ?? [];

            // Output to workflow variables for persistence across delays
            context.Set(PrepareCommandIdOut, id);
            context.Set(LinesJsonOut, linesJson);
            context.Set(FulfilmentIdOut, fulfilmentId);

            Console.WriteLine();
            Console.WriteLine($"[PreparationWorkflow] Received PrepareCommand id={id}  store={storeId}  lines={lines.Count}  delay={DelaySeconds}s  fulfilmentId={fulfilmentId}");
            foreach (var line in lines)
                Console.WriteLine($"  {line.Sku,-20} qty:{line.Quantity,3}  {line.UnitOfMeasure}");
            
            if (lines.Count == 0)
            {
                Console.WriteLine("  ⚠️  WARNING: No order lines found - will result in 0 containers");
            }
            else
            {
                Console.WriteLine($"  ✓ Stored {lines.Count} lines in workflow variables for persistence across delay");
            }

            await context.CompleteActivityAsync();
        }
        catch (ObjectDisposedException ex)
        {
            Console.WriteLine($"[PreparationWorkflow] Service disposed during LogPreparation execution: {ex.ObjectName} - Logging completed");
            // Don't re-throw - the logging work was completed
        }
        catch (Exception ex) when (ex.ToString().Contains("IServiceProvider"))
        {
            Console.WriteLine($"[PreparationWorkflow] Service provider disposed during LogPreparation - Logging completed: {ex.Message}");
            // Don't re-throw - the logging work was completed
        }
    }
}
