using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Workflows;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Services.Preparation;

internal class LogPreparationRequestActivity : Activity
{
    public Output<string>?   PrepareCommandIdOut      { get; set; }
    public Output<string>?   LinesJsonOut             { get; set; }
    public Output<string>?   FulfilmentIdOut          { get; set; }
    public Output<string>?   ParentWorkflowInstanceIdOut { get; set; }
    /// <summary>Per-instance random delay duration, computed here so the Delay activity
    /// receives a variable value rather than one baked in at workflow registration time.</summary>
    public Output<TimeSpan>? DelayOut                 { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var input = context.WorkflowExecutionContext.Input;

        object? idVal = null, storeIdVal = null, linesVal = null, fulfilmentIdVal = null, parentInstanceIdVal = null;
        input?.TryGetValue("PrepareCommandId",        out idVal);
        input?.TryGetValue("StoreId",                 out storeIdVal);
        input?.TryGetValue("Lines",                   out linesVal);
        input?.TryGetValue("FulfilmentId",            out fulfilmentIdVal);
        input?.TryGetValue("ParentWorkflowInstanceId", out parentInstanceIdVal);

        var id               = idVal?.ToString()             ?? "(unknown)";
        var storeId          = storeIdVal?.ToString()        ?? "(unknown)";
        var linesJson        = linesVal?.ToString()          ?? "[]";
        var fulfilmentId     = fulfilmentIdVal?.ToString()   ?? "(unknown)";
        var parentInstanceId = parentInstanceIdVal?.ToString() ?? "(unknown)";
        var lines            = JsonSerializer.Deserialize<List<OrderLine>>(linesJson) ?? [];

        // Compute per-instance delay here (execution time), not in Build() (registration time).
        var randomSeconds = Random.Shared.Next(5, 31);
        var delay         = TimeSpan.FromSeconds(randomSeconds);

        context.Set(PrepareCommandIdOut,         id);
        context.Set(LinesJsonOut,                linesJson);
        context.Set(FulfilmentIdOut,             fulfilmentId);
        context.Set(ParentWorkflowInstanceIdOut, parentInstanceId);
        context.Set(DelayOut,                    delay);

        Console.WriteLine();
        Console.WriteLine($"[PreparationWorkflow] id={id}  store={storeId}  lines={lines.Count}  delay={randomSeconds}s  fulfilmentId={fulfilmentId}");
        foreach (var line in lines)
            Console.WriteLine($"  {line.Sku,-20} qty:{line.Quantity,3}  {line.UnitOfMeasure}");

        if (lines.Count == 0)
            Console.WriteLine("  ⚠️  No order lines — will produce 0 containers");

        await context.CompleteActivityAsync();
    }
}
