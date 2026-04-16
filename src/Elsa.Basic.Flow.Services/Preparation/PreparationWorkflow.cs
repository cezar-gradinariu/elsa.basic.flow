using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Scheduling.Activities;

namespace Elsa.Basic.Flow.Services.Preparation;

public class PreparationWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var randomSeconds = Random.Shared.Next(5, 31);
        var prepareCommandId = builder.WithVariable<string>();
        var linesJson = builder.WithVariable<string>();
        
        builder.Root = new Sequence
        {
            Activities =
            [
                new LogPreparationRequestActivity 
                { 
                    DelaySeconds = randomSeconds,
                    PrepareCommandIdOut = new(prepareCommandId),
                    LinesJsonOut = new(linesJson)
                },
                new WriteLine($"[PreparationWorkflow] Starting delay of {randomSeconds} seconds..."),
                new Delay(TimeSpan.FromSeconds(randomSeconds)),
                new WriteLine($"[PreparationWorkflow] Delay of {randomSeconds} seconds completed - building outcome payload"),
                new CreateAndSendPreparationOutcomeActivity
                {
                    PrepareCommandIdIn = new(prepareCommandId),
                    LinesJsonIn = new(linesJson)
                }
            ]
        };
    }
}
