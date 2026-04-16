using Elsa.Basic.Flow.Scenarios.ParallelSubflows.Activities;
using Elsa.Extensions;
using Elsa.Features.Services;
using Elsa.Workflows;
using Elsa.Workflows.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Basic.Flow.Scenarios.ParallelSubflows;

/// <summary>
/// Drives the ParallelDetailsWorkflow one bookmark at a time.
/// Both Fork branches are already running concurrently in the workflow state;
/// we surface their prompts sequentially because the console is a single stream.
/// </summary>
public class ParallelDetailsScenario : IWorkflowScenario
{
    public string Name => "Parallel Details";
    public string Description => "Two sub-workflows in parallel: Person (name/surname) + Address (street/suburb)";

    public void Register(IModule elsa) => elsa.AddWorkflow<ParallelDetailsWorkflow>();

    public async Task RunAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var runner = services.GetRequiredService<IWorkflowRunner>();

        // Initial run — both Fork branches execute until each hits its first ConsolePrompt.
        var result = await runner.RunAsync<ParallelDetailsWorkflow>(cancellationToken: ct);
        var state  = result.WorkflowState;

        // Process bookmarks until the workflow finishes.
        // When only one is pending it is answered automatically.
        // When multiple are pending the user picks which sub-workflow to answer next.
        while (state.Bookmarks.Count > 0)
        {
            var bookmarks = state.Bookmarks.ToList();

            Elsa.Workflows.Models.Bookmark chosen;
            if (bookmarks.Count == 1)
            {
                chosen = bookmarks[0];
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("Pending inputs — pick which to answer:");
                for (var i = 0; i < bookmarks.Count; i++)
                    Console.WriteLine($"  [{i + 1}] {ConsolePrompt.PromptFrom(bookmarks[i])}");
                Console.Write("Choice: ");

                var pick = int.TryParse(Console.ReadLine(), out var n) ? n - 1 : 0;
                chosen = bookmarks[Math.Clamp(pick, 0, bookmarks.Count - 1)];
            }

            Console.Write(ConsolePrompt.PromptFrom(chosen));
            var input = Console.ReadLine() ?? string.Empty;

            result = await runner.RunAsync(result.Workflow, state, new RunWorkflowOptions
            {
                BookmarkId = chosen.Id,
                Input      = new Dictionary<string, object> { ["UserInput"] = input }
            }, ct);

            state = result.WorkflowState;
        }
    }
}
