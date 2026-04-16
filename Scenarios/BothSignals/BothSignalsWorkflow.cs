using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Runtime.Activities;

namespace Elsa.Basic.Flow.Scenarios.BothSignals;

/// <summary>
/// Demonstrates Fork WaitAll: the workflow suspends in two branches simultaneously
/// and proceeds only after both named signals have been received.
/// </summary>
public class BothSignalsWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Root = new Sequence
        {
            Activities =
            [
                new WriteLine("=== Both Signals Scenario ==="),
                new WriteLine("Waiting for both 'signal.alpha' AND 'signal.beta' before proceeding..."),
                new Fork
                {
                    JoinMode = ForkJoinMode.WaitAll,
                    Branches =
                    [
                        new Sequence
                        {
                            Activities =
                            [
                                new Event("signal.alpha"),
                                new WriteLine("Alpha received — waiting for Beta...")
                            ]
                        },
                        new Sequence
                        {
                            Activities =
                            [
                                new Event("signal.beta"),
                                new WriteLine("Beta received — waiting for Alpha...")
                            ]
                        }
                    ]
                },
                new WriteLine("Both signals received. Proceeding."),
                new WriteLine("Workflow completed.")
            ]
        };
    }
}
