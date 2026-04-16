using Elsa.Workflows;
using Elsa.Workflows.Activities;

namespace Elsa.Basic.Flow.Services.Preparation;

public class PreparationWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Root = new Sequence
        {
            Activities =
            [
                new LogPreparationRequestActivity(),
                new RandomDelayActivity(),
                new CreateAndSendPreparationOutcomeActivity()
            ]
        };
    }
}
