using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Scenarios.Greeting.Activities;

/// <summary>
/// Prints a prompt to the console and suspends the workflow until the host
/// resumes it — either with a user's answer or with a timeout signal.
/// </summary>
[Activity("BasicFlow", "Console", "Suspends and waits for console input.")]
public class AskInput : Activity
{
    public string Prompt { get; set; } = "Enter a value: ";

    [Output] public Output<string> Result { get; set; } = new();
    [Output] public Output<bool> TimedOut { get; set; } = new();

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        Console.Write(Prompt);
        context.CreateBookmark(new CreateBookmarkArgs
        {
            BookmarkName = "ConsoleInput",
            Callback = OnResumedAsync,
            AutoBurn = true
        });
    }

    private async ValueTask OnResumedAsync(ActivityExecutionContext context)
    {
        var wfInput = context.WorkflowExecutionContext.Input;

        var timedOut = wfInput.TryGetValue("TimedOut", out var tVal) && tVal is true;
        context.Set(TimedOut, timedOut);

        if (!timedOut)
        {
            var userInput = wfInput.TryGetValue("UserInput", out var uVal)
                ? uVal?.ToString() ?? string.Empty
                : string.Empty;
            context.Set(Result, userInput);
        }

        await context.CompleteActivityAsync();
    }
}
