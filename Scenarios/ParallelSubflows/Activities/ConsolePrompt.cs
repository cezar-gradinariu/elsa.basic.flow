using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Scenarios.ParallelSubflows.Activities;

/// <summary>
/// Suspends the workflow waiting for console input.
/// Unlike AskInput, this activity does NOT write the prompt itself —
/// the prompt is stored in the bookmark payload so the scenario runner
/// can display it at the right moment when multiple bookmarks are pending.
/// </summary>
[Activity("BasicFlow", "Console", "Suspends waiting for typed input; prompt is stored in the bookmark.")]
public class ConsolePrompt : Activity
{
    public string Prompt { get; set; } = "Enter a value: ";

    [Output]
    public Output<string> Result { get; set; } = new();

    // Prefix used to encode the prompt text inside the bookmark name.
    private const string Prefix = "ConsolePrompt::";

    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        // Encode the prompt in BookmarkName — Bookmark.Name is readable from WorkflowState
        // and CreateBookmarkArgs has no Payload property in this Elsa version.
        context.CreateBookmark(new CreateBookmarkArgs
        {
            BookmarkName = Prefix + Prompt,
            Callback = OnResumedAsync,
            AutoBurn = true
        });
        return ValueTask.CompletedTask;
    }

    /// <summary>Extracts the prompt text from a bookmark created by this activity.</summary>
    public static string PromptFrom(Elsa.Workflows.Models.Bookmark bookmark) =>
        bookmark.Name.StartsWith(Prefix) ? bookmark.Name[Prefix.Length..] : "Enter value: ";

    private async ValueTask OnResumedAsync(ActivityExecutionContext context)
    {
        var input = context.WorkflowExecutionContext.Input;
        var value = input.TryGetValue("UserInput", out var v) ? v?.ToString() ?? string.Empty : string.Empty;
        context.Set(Result, value);
        await context.CompleteActivityAsync();
    }
}
