using Elsa.Basic.Flow.Services.Preparation.Activities;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Services.Fulfilment.Workflows;

public class StorePreparationSubWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var sendSucceeded = builder.WithVariable<bool>();

        builder.Root = new Sequence
        {
            Activities =
            [
                new InitialiseStorePreparationPropertiesActivity(),
                new SendStorePreparationCommandActivity
                {
                    Succeeded = new Output<bool>(sendSucceeded)
                },
                new If(sendSucceeded.Get)
                {
                    Then = new WaitForStorePreparationOutcomeActivity(),
                    Else = new NotifyParentOfFaultActivity()
                }
            ]
        };
    }
}
