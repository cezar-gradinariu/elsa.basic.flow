using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Scenarios.CountdownTimer.Activities;

/// <summary>
/// Suspends the workflow with a named bookmark that encodes the current
/// timeout in seconds.  On resume:
///   - Action="reset", Seconds=N  → creates a NEW bookmark with the new
///     timeout and does NOT complete — the activity stays alive.
///   - Action="expired" (or anything else) → completes the activity.
///
/// This lets the scenario loop without any While activity in the workflow.
/// </summary>
[Activity("BasicFlow", "Console", "Suspends until a countdown expires or is reset by the user.")]
public class SuspendForCountdown : Activity
{
    private const string Prefix = "Countdown::";

    /// <summary>Initial timeout in seconds (default 40).</summary>
    public int DefaultSeconds { get; set; } = 40;

    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        Suspend(context, DefaultSeconds);
        return ValueTask.CompletedTask;
    }

    // ── static so it can be referenced as a delegate from both entry-points ──

    private static void Suspend(ActivityExecutionContext context, int seconds)
    {
        context.CreateBookmark(new CreateBookmarkArgs
        {
            BookmarkName = Prefix + seconds,
            Callback     = ResumeAsync,
            AutoBurn     = true
        });
    }

    private static async ValueTask ResumeAsync(ActivityExecutionContext context)
    {
        var input  = context.WorkflowExecutionContext.Input;
        var action = input.TryGetValue("Action", out var a) ? a?.ToString() : null;

        if (action == "reset"
            && input.TryGetValue("Seconds", out var s)
            && int.TryParse(s?.ToString(), out var n)
            && n > 0)
        {
            // Re-suspend with the new timeout — activity stays alive.
            Suspend(context, n);
        }
        else
        {
            // Timer expired — let the workflow move to the next activity.
            await context.CompleteActivityAsync();
        }
    }

    /// <summary>Extracts the current timeout (in seconds) from a Countdown bookmark.</summary>
    public static int SecondsFrom(Bookmark bookmark)
    {
        var name = bookmark.Name ?? string.Empty;
        return name.StartsWith(Prefix) && int.TryParse(name[Prefix.Length..], out var n) ? n : 40;
    }
}
