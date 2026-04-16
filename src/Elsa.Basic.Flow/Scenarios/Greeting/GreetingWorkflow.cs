using Elsa.Basic.Flow.Scenarios.Greeting.Activities;
using Elsa.Workflows;
using Elsa.Workflows.Activities;

namespace Elsa.Basic.Flow.Scenarios.Greeting;

/// <summary>
/// Asks for the user's name and greets them.
/// If they don't respond within 20 seconds the workflow notifies them and exits.
/// </summary>
public class GreetingWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var name = builder.WithVariable<string>();
        var timedOut = builder.WithVariable<bool>();

        builder.Root = new Sequence
        {
            Activities =
            [
                new WriteLine("=== Greeting Scenario ==="),
                new AskInput
                {
                    Prompt = "What is your name? ",
                    Result = new(name),
                    TimedOut = new(timedOut)
                },
                new If(context => timedOut.Get(context))
                {
                    Then = new WriteLine("Time's up — the workflow will now complete. Goodbye!"),
                    Else = new Sequence
                    {
                        Activities =
                        [
                            new WriteLine(context => $"Hello, {name.Get(context)}! Welcome to the workflow."),
                            new WriteLine("Workflow completed.")
                        ]
                    }
                }
            ]
        };
    }
}
