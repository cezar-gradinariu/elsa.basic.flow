using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Models;
using Elsa.Scheduling.Activities;

namespace Elsa.Basic.Flow.Services.Preparation;

public class PreparationWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var prepareCommandId = builder.WithVariable<string>();
        var linesJson        = builder.WithVariable<string>();
        var delayDuration    = builder.WithVariable<TimeSpan>();

        builder.Root = new Sequence
        {
            Activities =
            [
                new LogPreparationRequestActivity
                {
                    PrepareCommandIdOut = new Output<string>(prepareCommandId),
                    LinesJsonOut        = new Output<string>(linesJson),
                    DelayOut            = new Output<TimeSpan>(delayDuration)
                },
                new Delay(delayDuration),
                new CreateAndSendPreparationOutcomeActivity
                {
                    PrepareCommandIdIn = new Input<string>(prepareCommandId),
                    LinesJsonIn        = new Input<string>(linesJson)
                }
            ]
        };
    }
}
