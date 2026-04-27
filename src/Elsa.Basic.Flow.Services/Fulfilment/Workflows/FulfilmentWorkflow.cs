using Elsa.Basic.Flow.Services.Allocation;
using Elsa.Basic.Flow.Services.Preparation;
using Elsa.Scheduling.Activities;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Services.Fulfilment;

public class FulfilmentWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var fulfilmentId      = builder.WithVariable<string>();
        var allocationResult  = builder.WithVariable<AllocationResult>();
        var prepareCommands   = builder.WithVariable<List<PrepareCommand>>();
        var allocationSuccess = builder.WithVariable<bool>(false);
        var nextDelay         = builder.WithVariable<TimeSpan>();

        builder.Root = new Sequence
        {
            Activities =
            [
                new InitialiseFulfilmentPropertiesActivity(),

                // Retry loop: attempt allocation up to MaxAttempts times, with
                // durable Delay between failures. AllocateFulfilmentActivity
                // throws after MaxAttempts exhaustion — no need for post-loop check.
                new While(new Sequence
                {
                    Activities =
                    [
                        new AllocateFulfilmentActivity
                        {
                            FulfilmentId = new Output<string>(fulfilmentId),
                            Result       = new Output<AllocationResult>(allocationResult),
                            Succeeded    = new Output<bool>(allocationSuccess),
                            NextDelay    = new Output<TimeSpan>(nextDelay)
                        },
                        new If(ctx => !allocationSuccess.Get(ctx))
                        {
                            Then = new Delay(nextDelay)
                        }
                    ]
                })
                {
                    Condition = new Input<bool>(ctx => !allocationSuccess.Get(ctx))
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
