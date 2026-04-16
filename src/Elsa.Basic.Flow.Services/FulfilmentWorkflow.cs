using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Services;

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
                new LogPrepareCommandsActivity
                {
                    AllocationResult = new Input<AllocationResult>(allocationResult)
                }
            ]
        };
    }
}
