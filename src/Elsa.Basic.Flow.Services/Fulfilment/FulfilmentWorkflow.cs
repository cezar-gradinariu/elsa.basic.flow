using Elsa.Basic.Flow.Services.Allocation;
using Elsa.Basic.Flow.Services.Preparation;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Services.Fulfilment;

public class FulfilmentWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var allocationResult = builder.WithVariable<AllocationResult>();
        var prepareCommands  = builder.WithVariable<List<PrepareCommand>>();

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
                    PrepareCommands  = new Output<List<PrepareCommand>>(prepareCommands)
                },
                // Registers the tracker and creates the Event bookmark atomically,
                // then suspends until all preparations signal completion.
                new WaitForPreparationsActivity
                {
                    PrepareCommands = new Input<List<PrepareCommand>>(prepareCommands)
                },
                new CompleteFulfilmentActivity()
            ]
        };
    }
}
