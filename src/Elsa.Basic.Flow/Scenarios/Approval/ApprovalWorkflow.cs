using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Runtime.Activities;

namespace Elsa.Basic.Flow.Scenarios.Approval;

/// <summary>
/// Waits for either a "signal.approved" or "signal.rejected" external event
/// then prints the appropriate outcome message.
/// </summary>
public class ApprovalWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var decision = builder.WithVariable<string>();

        builder.Root = new Sequence
        {
            Activities =
            [
                new WriteLine("=== Approval Scenario ==="),
                new WriteLine("Request submitted. Waiting for an external decision signal..."),

                new Fork
                {
                    JoinMode = ForkJoinMode.WaitAny,
                    Branches =
                    [
                        new Sequence
                        {
                            Activities =
                            [
                                new Event("signal.approved"),
                                new SetVariable<string> { Variable = decision, Value = new("approved") }
                            ]
                        },
                        new Sequence
                        {
                            Activities =
                            [
                                new Event("signal.rejected"),
                                new SetVariable<string> { Variable = decision, Value = new("rejected") }
                            ]
                        }
                    ]
                },

                new If(context => decision.Get(context) == "approved")
                {
                    Then = new Sequence
                    {
                        Activities =
                        [
                            new WriteLine("Decision received: APPROVED"),
                            new WriteLine("The request has been approved. Processing will proceed.")
                        ]
                    },
                    Else = new Sequence
                    {
                        Activities =
                        [
                            new WriteLine("Decision received: REJECTED"),
                            new WriteLine("The request has been rejected. No further processing.")
                        ]
                    }
                },

                new WriteLine("Workflow completed.")
            ]
        };
    }
}
