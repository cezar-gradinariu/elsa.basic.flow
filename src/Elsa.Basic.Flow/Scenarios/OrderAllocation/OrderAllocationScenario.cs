using Elsa.Basic.Flow.Scenarios.OrderAllocation.Activities;
using Elsa.Extensions;
using Elsa.Features.Services;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Elsa.Workflows.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Basic.Flow.Scenarios.OrderAllocation;

/// <summary>
/// Drives OrderAllocationWorkflow one bookmark at a time.
/// When multiple bookmarks are pending (both stores waiting for a decision)
/// the user picks which store to answer first.
/// </summary>
public class OrderAllocationScenario : IWorkflowScenario
{
    public string Name => "Order Allocation";
    public string Description => "Generate an order, allocate lines to stores, accept/reject each in parallel";

    public void Register(IModule elsa) => elsa.AddWorkflow<OrderAllocationWorkflow>();

    public async Task RunAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var runner = services.GetRequiredService<IWorkflowRunner>();

        var result = await runner.RunAsync<OrderAllocationWorkflow>(cancellationToken: ct);
        var state  = result.WorkflowState;

        while (state.Bookmarks.Count > 0)
        {
            var bookmarks = state.Bookmarks.ToList();

            Bookmark chosen;
            if (bookmarks.Count == 1)
            {
                chosen = bookmarks[0];
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("Pending decisions — pick which store to answer:");
                for (var i = 0; i < bookmarks.Count; i++)
                    Console.WriteLine($"  [{i + 1}] {PromptFrom(bookmarks[i])}");
                Console.Write("Choice: ");

                var pick = int.TryParse(Console.ReadLine(), out var n) ? n - 1 : 0;
                chosen = bookmarks[Math.Clamp(pick, 0, bookmarks.Count - 1)];
            }

            Console.Write(PromptFrom(chosen));
            var input = Console.ReadLine() ?? string.Empty;

            result = await runner.RunAsync(result.Workflow, state, new RunWorkflowOptions
            {
                BookmarkId = chosen.Id,
                Input      = new Dictionary<string, object> { ["UserInput"] = input }
            }, ct);

            state = result.WorkflowState;
        }
    }

    // Strips the "XxxActivity::" prefix encoded by PromptInt and PromptAccept.
    private static string PromptFrom(Bookmark bookmark)
    {
        var name = bookmark.Name ?? "";
        var sep  = name.IndexOf("::", StringComparison.Ordinal);
        return sep >= 0 ? name[(sep + 2)..] : "Enter value: ";
    }
}
