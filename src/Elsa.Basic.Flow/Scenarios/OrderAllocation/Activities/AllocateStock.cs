using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Scenarios.OrderAllocation.Activities;

/// <summary>
/// Simulates an external stock-allocation API call.
/// Each order line is randomly assigned to store 1000 or 2000,
/// with a guarantee that each store receives at least one line.
/// </summary>
[Activity("BasicFlow", "Order", "Simulates allocating order lines to stores via an external API.")]
public class AllocateStock : Activity
{
    private static readonly int[] StoreIds = [1000, 2000];

    public Input<Order>? Order { get; set; }

    [Output] public Output<List<AllocatedLine>> Result { get; set; } = new();

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var order = context.Get(Order)!;
        var allocated = order.Lines
            .Select(l => new AllocatedLine(l.ArticleId, l.Name, l.Quantity,
                StoreId: StoreIds[Random.Shared.Next(StoreIds.Length)]))
            .ToList();

        // Guarantee each store has at least one line when there are 2+ lines.
        if (allocated.Count >= 2 && allocated.All(l => l.StoreId == allocated[0].StoreId))
        {
            var last = allocated[^1];
            var otherId = StoreIds.First(id => id != last.StoreId);
            allocated[^1] = last with { StoreId = otherId };
        }

        Console.WriteLine();
        Console.WriteLine("[API] Stock allocation response:");
        foreach (var l in allocated)
            Console.WriteLine($"  {l.ArticleId,-8} → Store {l.StoreId}");

        context.Set(Result, allocated);
        await context.CompleteActivityAsync();
    }
}
