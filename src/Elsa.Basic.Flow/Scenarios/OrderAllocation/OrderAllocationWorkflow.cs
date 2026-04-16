using Elsa.Basic.Flow.Scenarios.OrderAllocation.Activities;
using Elsa.Workflows;
using Elsa.Workflows.Activities;

namespace Elsa.Basic.Flow.Scenarios.OrderAllocation;

/// <summary>
/// End-to-end order allocation flow:
///   1. Ask how many order lines to create.
///   2. Generate an order with that many random lines.
///   3. Simulate an API call that allocates lines to stores 1000 and 2000.
///   4. Split the result into per-store groups.
///   5. Run two sub-workflows in parallel (Fork WaitAll), one per store:
///      each shows its lines and waits for a Y/N acceptance decision.
///   6. Print the final outcome once both sub-workflows complete.
/// </summary>
public class OrderAllocationWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var count       = builder.WithVariable<int>();
        var order       = builder.WithVariable<Order>();
        var allocated   = builder.WithVariable<List<AllocatedLine>>();
        var store1Group = builder.WithVariable<StoreGroup>();
        var store2Group = builder.WithVariable<StoreGroup>();
        var accepted1   = builder.WithVariable<bool>();
        var accepted2   = builder.WithVariable<bool>();

        builder.Root = new Sequence
        {
            Activities =
            [
                new WriteLine("=== Order Allocation Scenario ==="),

                // Step 1 — ask for order size
                new PromptInt
                {
                    Prompt = "How many order lines? ",
                    Result = new(count)
                },

                // Step 2 — generate order
                new GenerateOrder
                {
                    Count  = new(count),
                    Result = new(order)
                },

                // Step 3 — simulate allocation API
                new AllocateStock
                {
                    Order  = new(order),
                    Result = new(allocated)
                },

                // Step 4 — split into per-store groups
                new SplitByStore
                {
                    AllocationResult = new(allocated),
                    Store1           = new(store1Group),
                    Store2           = new(store2Group)
                },

                // Step 5 — parallel sub-workflow: one per store, same definition
                new Fork
                {
                    JoinMode = ForkJoinMode.WaitAll,
                    Branches =
                    [
                        // Sub-workflow: Store 1000
                        new Sequence
                        {
                            Activities =
                            [
                                new ShowStoreLines { Group = new(store1Group) },
                                new PromptAccept   { Group = new(store1Group), Result = new(accepted1) }
                            ]
                        },

                        // Sub-workflow: Store 2000
                        new Sequence
                        {
                            Activities =
                            [
                                new ShowStoreLines { Group = new(store2Group) },
                                new PromptAccept   { Group = new(store2Group), Result = new(accepted2) }
                            ]
                        }
                    ]
                },

                // Step 6 — final outcome
                new WriteLine(""),
                new WriteLine("════ Allocation outcome ════════════════════════════"),
                new WriteLine(ctx => $"  Store 1000 : {(accepted1.Get(ctx) ? "ACCEPTED ✓" : "REJECTED ✗")}"),
                new WriteLine(ctx => $"  Store 2000 : {(accepted2.Get(ctx) ? "ACCEPTED ✓" : "REJECTED ✗")}"),
                new WriteLine("════════════════════════════════════════════════════"),
                new WriteLine("Workflow completed.")
            ]
        };
    }
}
