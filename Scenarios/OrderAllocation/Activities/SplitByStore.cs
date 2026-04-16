using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Scenarios.OrderAllocation.Activities;

/// <summary>Splits a flat allocation result into one StoreGroup per store.</summary>
[Activity("BasicFlow", "Order", "Splits allocated lines into per-store groups.")]
public class SplitByStore : Activity
{
    public Input<List<AllocatedLine>>? AllocationResult { get; set; }

    [Output] public Output<StoreGroup> Store1 { get; set; } = new();
    [Output] public Output<StoreGroup> Store2 { get; set; } = new();

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var lines = context.Get(AllocationResult)!;
        context.Set(Store1, new StoreGroup(1000, lines.Where(l => l.StoreId == 1000).ToList()));
        context.Set(Store2, new StoreGroup(2000, lines.Where(l => l.StoreId == 2000).ToList()));
        await context.CompleteActivityAsync();
    }
}
