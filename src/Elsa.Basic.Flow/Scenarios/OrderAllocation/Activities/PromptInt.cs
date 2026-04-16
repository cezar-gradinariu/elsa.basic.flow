using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Scenarios.OrderAllocation.Activities;

/// <summary>Suspends waiting for an integer typed by the user.</summary>
[Activity("BasicFlow", "Console", "Suspends waiting for an integer typed by the user.")]
public class PromptInt : Activity
{
    private const string Prefix = "PromptInt::";

    public string Prompt { get; set; } = "Enter a number: ";

    [Output] public Output<int> Result { get; set; } = new();

    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        context.CreateBookmark(new CreateBookmarkArgs
        {
            BookmarkName = Prefix + Prompt,
            Callback     = OnResumedAsync,
            AutoBurn     = true
        });
        return ValueTask.CompletedTask;
    }

    private async ValueTask OnResumedAsync(ActivityExecutionContext context)
    {
        var input = context.WorkflowExecutionContext.Input;
        var raw   = input.TryGetValue("UserInput", out var v) ? v?.ToString() ?? "1" : "1";
        context.Set(Result, int.TryParse(raw, out var n) ? Math.Max(1, n) : 1);
        await context.CompleteActivityAsync();
    }

    public static string PromptFrom(Bookmark bookmark) =>
        bookmark.Name.StartsWith(Prefix) ? bookmark.Name[Prefix.Length..] : "Enter a number: ";
}
