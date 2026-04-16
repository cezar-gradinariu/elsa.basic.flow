using Elsa.Basic.Flow.Scenarios.CountdownTimer.Activities;
using Elsa.Extensions;
using Elsa.Features.Services;
using Elsa.Workflows;
using Elsa.Workflows.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Basic.Flow.Scenarios.CountdownTimer;

/// <summary>
/// Drives CountdownTimerWorkflow one bookmark at a time.
///
/// Each iteration races two tasks:
///   1. Console.ReadLine — user types a new number of seconds.
///   2. Task.Delay(currentSeconds) — the countdown fires.
///
/// Whichever wins first determines the resume signal sent to the workflow:
///   • User input  → Action="reset", Seconds=N  (workflow re-suspends)
///   • Timer fires → Action="expired"            (workflow completes)
/// </summary>
public class CountdownTimerScenario : IWorkflowScenario
{
    public string Name        => "Countdown Timer";
    public string Description => "Wait 40 s by default; type a new number to reset the timer repeatedly";

    public void Register(IModule elsa) => elsa.AddWorkflow<CountdownTimerWorkflow>();

    public async Task RunAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var runner = services.GetRequiredService<IWorkflowRunner>();

        // First run — executes until SuspendForCountdown creates its bookmark.
        var result = await runner.RunAsync<CountdownTimerWorkflow>(cancellationToken: ct);
        var state  = result.WorkflowState;

        // One background reader shared across iterations (Console.ReadLine blocks
        // and cannot be cancelled, so we reuse / replace it carefully).
        var inputTask = Task.Run(() => Console.ReadLine(), ct);

        while (state.Bookmarks.Count > 0)
        {
            var bookmark = state.Bookmarks.First();
            var seconds  = SuspendForCountdown.SecondsFrom(bookmark);

            Console.WriteLine();
            Console.WriteLine($"[Timer running: {seconds}s — type a number to reset]");

            using var timerCts = new CancellationTokenSource();
            var timerTask = Task.Delay(TimeSpan.FromSeconds(seconds), timerCts.Token);

            Dictionary<string, object> resumeInput;

            if (await Task.WhenAny(inputTask, timerTask) == inputTask)
            {
                // User typed something before the timer fired.
                timerCts.Cancel();
                var raw        = (await inputTask)?.Trim() ?? string.Empty;
                var newSeconds = int.TryParse(raw, out var n) && n > 0 ? n : seconds;

                Console.WriteLine($"[Timer reset to {newSeconds}s]");
                resumeInput = new Dictionary<string, object>
                {
                    ["Action"]  = "reset",
                    ["Seconds"] = newSeconds.ToString()
                };

                // Prepare a fresh reader for the next iteration.
                inputTask = Task.Run(() => Console.ReadLine(), ct);
            }
            else
            {
                // Timer expired before the user typed anything.
                timerCts.Cancel();
                Console.WriteLine($"\n[Timer expired after {seconds}s]");
                resumeInput = new Dictionary<string, object> { ["Action"] = "expired" };
                // inputTask is still pending but the workflow will finish,
                // so it will be abandoned when the process exits.
            }

            result = await runner.RunAsync(result.Workflow, state, new RunWorkflowOptions
            {
                BookmarkId = bookmark.Id,
                Input      = resumeInput
            }, ct);

            state = result.WorkflowState;
        }
    }
}
