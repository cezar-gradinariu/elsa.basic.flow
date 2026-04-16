using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Scenarios.OrderAllocation.Activities;

/// <summary>Prints the lines allocated to a store.</summary>
[Activity("BasicFlow", "Order", "Prints the lines allocated to a store.")]
public class ShowStoreLines : Activity
{
    public Input<StoreGroup>? Group { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var group = context.Get(Group)!;
        Console.WriteLine();
        Console.WriteLine($"── Store {group.StoreId} ({group.Lines.Count} line(s)) ──────────────────");
        foreach (var l in group.Lines)
            Console.WriteLine($"  {l.ArticleId,-8} {l.Name,-22} qty: {l.Quantity}");

        await context.CompleteActivityAsync();
    }
}
