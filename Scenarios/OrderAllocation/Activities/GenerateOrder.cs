using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Scenarios.OrderAllocation.Activities;

/// <summary>Builds a random Order with the requested number of lines.</summary>
[Activity("BasicFlow", "Order", "Generates a random order with N lines.")]
public class GenerateOrder : Activity
{
    private static readonly string[] Names =
    [
        "Widget Alpha", "Widget Beta", "Gadget Gamma", "Gadget Delta",
        "Sprocket Epsilon", "Doohickey Zeta", "Thingamajig Eta", "Whatsit Theta"
    ];

    public Input<int>? Count { get; set; }

    [Output] public Output<Order> Result { get; set; } = new();

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var count = context.Get(Count);
        var lines = Enumerable.Range(1, count)
            .Select(i => new OrderLine(
                ArticleId: $"ART{i:D3}",
                Name:      Names[(i - 1) % Names.Length],
                Quantity:  Random.Shared.Next(1, 20)))
            .ToList();

        var order = new Order(Id: $"ORD-{Random.Shared.Next(10000, 99999)}", Lines: lines);

        Console.WriteLine();
        Console.WriteLine($"Order {order.Id} created — {order.Lines.Count} line(s):");
        foreach (var l in order.Lines)
            Console.WriteLine($"  {l.ArticleId,-8} {l.Name,-22} qty: {l.Quantity}");

        context.Set(Result, order);
        await context.CompleteActivityAsync();
    }
}
