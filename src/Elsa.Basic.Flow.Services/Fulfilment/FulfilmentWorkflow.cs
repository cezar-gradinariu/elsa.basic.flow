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
        var fulfilmentId     = builder.WithVariable<string>();
        var allocationResult = builder.WithVariable<AllocationResult>();
        var prepareCommands  = builder.WithVariable<List<PrepareCommand>>();

        builder.Root = new Sequence
        {
            Activities =
            [
                new AllocateFulfilmentActivity
                {
                    FulfilmentId = new Output<string>(fulfilmentId),
                    Result       = new Output<AllocationResult>(allocationResult)
                },
                new SendPrepareCommandsActivity
                {
                    AllocationResult = new Input<AllocationResult>(allocationResult),
                    FulfilmentId     = new Input<string>(fulfilmentId),
                    PrepareCommands  = new Output<List<PrepareCommand>>(prepareCommands)
                },
                new WaitForPreparationsActivity
                {
                    PrepareCommands = new Input<List<PrepareCommand>>(prepareCommands),
                    FulfilmentId    = new Input<string>(fulfilmentId)
                },
                new CompleteFulfilmentActivity
                {
                    FulfilmentId = new Input<string>(fulfilmentId)
                }
            ]
        };
    }
}
