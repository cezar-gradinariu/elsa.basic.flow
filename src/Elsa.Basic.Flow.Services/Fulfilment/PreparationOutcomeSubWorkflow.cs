using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Services.Fulfilment;

public class PreparationOutcomeSubWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var fulfilmentId = builder.WithVariable<string>();
        var prepareCommandId = builder.WithVariable<string>();
        var preparationOutcome = builder.WithVariable<string>();

        builder.Root = new Sequence
        {
            Activities =
            [
                new WriteLine("[PreparationOutcomeSubWorkflow] Sub-workflow started, waiting for outcome trigger..."),
                
                // Store outcome data in variables
                new StorePreparationOutcomeActivity
                {
                    FulfilmentIdOut = new(fulfilmentId),
                    PrepareCommandIdOut = new(prepareCommandId),
                    PreparationOutcomeOut = new(preparationOutcome)
                },
                
                new WriteLine($"[PreparationOutcomeSubWorkflow] Received outcome, updating aggregate..."),
                
                // Update the aggregate with containers
                new UpdateAggregateWithContainersActivity
                {
                    FulfilmentIdIn = new(fulfilmentId),
                    PrepareCommandIdIn = new(prepareCommandId),
                    PreparationOutcomeIn = new(preparationOutcome)
                },
                
                new WriteLine($"[PreparationOutcomeSubWorkflow] Sub-workflow completed successfully")
            ]
        };
    }
}