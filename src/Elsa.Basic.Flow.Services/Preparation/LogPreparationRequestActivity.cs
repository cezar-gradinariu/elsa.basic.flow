using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Workflows;

namespace Elsa.Basic.Flow.Services.Preparation;

internal class LogPreparationRequestActivity : Activity
{
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var input = context.WorkflowExecutionContext.Input;

        object? idVal = null, storeIdVal = null, linesVal = null;
        input?.TryGetValue("PrepareCommandId", out idVal);
        input?.TryGetValue("StoreId",          out storeIdVal);
        input?.TryGetValue("Lines",            out linesVal);

        var id      = idVal?.ToString()     ?? "(unknown)";
        var storeId = storeIdVal?.ToString() ?? "(unknown)";
        var lines   = JsonSerializer.Deserialize<List<OrderLine>>(linesVal?.ToString() ?? "[]") ?? [];

        Console.WriteLine();
        Console.WriteLine($"[PreparationWorkflow] Received PrepareCommand id={id}  store={storeId}  lines={lines.Count}");
        foreach (var line in lines)
            Console.WriteLine($"  {line.Sku,-20} qty:{line.Quantity,3}  {line.UnitOfMeasure}");

        await context.CompleteActivityAsync();
    }
}
