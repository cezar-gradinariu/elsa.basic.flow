using Elsa.Workflows;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Services;

internal class LogPrepareCommandsActivity : Activity
{
    public Input<AllocationResult>? AllocationResult { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var result   = context.Get(AllocationResult)!;
        var commands = result.StoreAllocations
            .Select(a => new PrepareCommand(a.StoreId, a.Lines))
            .ToList();

        Console.WriteLine();
        Console.WriteLine($"[Workflow] {commands.Count} PrepareCommand(s) for order {result.OrderNo}:");

        foreach (var cmd in commands)
        {
            Console.WriteLine($"  → StoreId: {cmd.StoreId} | Lines: {cmd.Lines.Count}");
            foreach (var line in cmd.Lines)
                Console.WriteLine($"      {line.Sku,-20} qty:{line.Quantity,3}  {line.UnitOfMeasure}");
        }

        await context.CompleteActivityAsync();
    }
}
