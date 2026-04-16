using Elsa.Basic.Flow.Scenarios.CountdownTimer.Activities;
using Elsa.Workflows;
using Elsa.Workflows.Activities;

namespace Elsa.Basic.Flow.Scenarios.CountdownTimer;

/// <summary>
/// Starts a 40-second countdown.  The user can type a new number of seconds
/// at any time to reset the timer; this can be done repeatedly for as long as
/// the workflow is alive.  The workflow completes when the current timer
/// eventually expires without a reset.
/// </summary>
public class CountdownTimerWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Root = new Sequence
        {
            Activities =
            [
                new WriteLine("=== Countdown Timer ==="),
                new WriteLine("Default timeout: 40 seconds."),
                new WriteLine("Type a positive number and press Enter at any time to reset the timer."),
                new SuspendForCountdown { DefaultSeconds = 40 },
                new WriteLine("Timer expired. Workflow completed.")
            ]
        };
    }
}
