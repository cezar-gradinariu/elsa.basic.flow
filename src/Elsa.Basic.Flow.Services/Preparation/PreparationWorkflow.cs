using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Models;
using Elsa.Scheduling.Activities;

namespace Elsa.Basic.Flow.Services.Preparation;

public class PreparationWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        // All variables are per-instance — resolved at execution time, not at registration time.
        var prepareCommandId = builder.WithVariable<string>();
        var linesJson        = builder.WithVariable<string>();
        var fulfilmentId     = builder.WithVariable<string>();
        var delayDuration    = builder.WithVariable<TimeSpan>();

        builder.Root = new Sequence
        {
            Activities =
            [
                new LogPreparationRequestActivity
                {
                    PrepareCommandIdOut = new Output<string>(prepareCommandId),
                    LinesJsonOut        = new Output<string>(linesJson),
                    FulfilmentIdOut     = new Output<string>(fulfilmentId),
                    DelayOut            = new Output<TimeSpan>(delayDuration)
                },
                new WriteLine("[PreparationWorkflow] Starting delay..."),
                new Delay(delayDuration),
                new WriteLine("[PreparationWorkflow] Delay completed — building outcome payload"),
                new CreateAndSendPreparationOutcomeActivity
                {
                    PrepareCommandIdIn = new Input<string>(prepareCommandId),
                    LinesJsonIn        = new Input<string>(linesJson),
                    FulfilmentIdIn     = new Input<string>(fulfilmentId)
                }
            ]
        };
    }
}
