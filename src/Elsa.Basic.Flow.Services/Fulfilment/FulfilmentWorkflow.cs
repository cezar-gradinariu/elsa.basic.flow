using Elsa.Basic.Flow.Services.Allocation;
using Elsa.Basic.Flow.Services.Preparation;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Runtime.Activities;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Services.Fulfilment;

public class FulfilmentWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var allocationResult = builder.WithVariable<AllocationResult>();
        var prepareCommands = builder.WithVariable<List<PrepareCommand>>();
        var completedCount = builder.WithVariable<int>();

        builder.Root = new Sequence
        {
            Activities =
            [
                new AllocateFulfilmentActivity
                {
                    Result = new Output<AllocationResult>(allocationResult)
                },
                new SendPrepareCommandsActivity
                {
                    AllocationResult = new Input<AllocationResult>(allocationResult),
                    PrepareCommands = new Output<List<PrepareCommand>>(prepareCommands)
                },
                new WaitForPreparationsActivity
                {
                    PrepareCommands = new Input<List<PrepareCommand>>(prepareCommands),
                    CompletedCount = new Output<int>(completedCount)
                },
                new Event("preparations-all-completed"),
                new CompleteFulfilmentActivity()
            ]
        };
    }
}
