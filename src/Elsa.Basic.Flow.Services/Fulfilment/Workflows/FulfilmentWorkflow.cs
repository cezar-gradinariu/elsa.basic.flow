using Elsa.Basic.Flow.Services.Allocation;
using Elsa.Basic.Flow.Services.Fulfilment.Activities;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Services.Fulfilment.Workflows;

public class FulfilmentWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var fulfilmentId     = builder.WithVariable<string>();
        var allocationResult = builder.WithVariable<AllocationResult>();

        builder.Root = new Sequence
        {
            Activities =
            [
                new InitialiseFulfilmentPropertiesActivity(),
                new AllocateFulfilmentActivity
                {
                    FulfilmentId = new Output<string>(fulfilmentId),
                    Result       = new Output<AllocationResult>(allocationResult)
                },
                new DispatchAndWaitPreparationsActivity
                {
                    AllocationResult = new Input<AllocationResult>(allocationResult),
                    FulfilmentId     = new Input<string>(fulfilmentId)
                },
                new CompleteFulfilmentActivity
                {
                    FulfilmentId = new Input<string>(fulfilmentId)
                }
            ]
        };
    }
}
