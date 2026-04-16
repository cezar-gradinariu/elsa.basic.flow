using Elsa.Basic.Flow.Services.Allocation;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Services.Fulfilment;

public class FulfilmentWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var allocationResult = builder.WithVariable<AllocationResult>();

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
                    AllocationResult = new Input<AllocationResult>(allocationResult)
                }
            ]
        };
    }
}
