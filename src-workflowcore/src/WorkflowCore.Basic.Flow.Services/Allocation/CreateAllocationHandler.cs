using WorkflowCore.Basic.Flow.Domain;

namespace WorkflowCore.Basic.Flow.Services.Allocation;

public class CreateAllocationHandler
{
    private static readonly string[] StorePool = ["STORE-EAST", "STORE-WEST", "STORE-NORTH"];

    public AllocationResult Handle(CreateAllocationCommand cmd)
    {
        if (cmd.OrderLines.Count == 0)
            throw new ArgumentException("Cannot allocate an order with no order lines.", nameof(cmd));

        var storeCount = cmd.OrderLines.Count switch
        {
            1    => 1,
            <= 4 => 2,
            _    => 3
        };

        var stores = new List<string> { cmd.StoreId };
        stores.AddRange(
            StorePool
                .Where(s => s != cmd.StoreId)
                .OrderBy(_ => Random.Shared.Next())
                .Take(storeCount - 1));

        var buckets = stores.Select(_ => new List<OrderLine>()).ToList();
        for (var i = 0; i < cmd.OrderLines.Count; i++)
            buckets[i % stores.Count].Add(cmd.OrderLines[i]);

        var allocations = stores
            .Zip(buckets, (storeId, lines) => new StoreAllocation(storeId, lines))
            .ToList();

        return new AllocationResult(cmd.OrderNo, allocations);
    }
}
