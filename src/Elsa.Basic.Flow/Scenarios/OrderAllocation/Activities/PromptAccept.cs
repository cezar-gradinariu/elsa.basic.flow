using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Scenarios.OrderAllocation.Activities;

/// <summary>
/// Suspends waiting for a Y/N acceptance decision for a store's allocation.
/// The store ID is encoded in the bookmark name so the scenario runner can
/// display the right prompt when multiple stores are pending simultaneously.
/// </summary>
[Activity("BasicFlow", "Order", "Suspends waiting for Y/N acceptance of a store's allocation.")]
public class PromptAccept : Activity
{
    private const string Prefix = "PromptAccept::";

    public Input<StoreGroup>? Group { get; set; }

    [Output] public Output<bool> Result { get; set; } = new();

    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var group = context.Get(Group)!;
        context.CreateBookmark(new CreateBookmarkArgs
        {
            BookmarkName = $"{Prefix}[Store {group.StoreId}] Accept {group.Lines.Count} line(s)? [Y/N]: ",
            Callback     = OnResumedAsync,
            AutoBurn     = true
        });
        return ValueTask.CompletedTask;
    }

    private async ValueTask OnResumedAsync(ActivityExecutionContext context)
    {
        var input = context.WorkflowExecutionContext.Input;
        var raw   = input.TryGetValue("UserInput", out var v) ? v?.ToString() ?? "" : "";
        context.Set(Result, raw.Trim().StartsWith('y') || raw.Trim().StartsWith('Y'));
        await context.CompleteActivityAsync();
    }

    public static string PromptFrom(Bookmark bookmark) =>
        bookmark.Name.StartsWith(Prefix) ? bookmark.Name[Prefix.Length..] : "Accept? [Y/N]: ";
}
