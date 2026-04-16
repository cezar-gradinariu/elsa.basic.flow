using Elsa.Extensions;
using Elsa.Features.Services;
using Elsa.Workflows;
using Elsa.Workflows.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Basic.Flow.Scenarios.Greeting;

/// <summary>
/// Demonstrates the bookmark suspend/resume pattern with a console prompt and a
/// 20-second timeout that races against the user's input.
/// </summary>
public class GreetingScenario : IWorkflowScenario
{
    public string Name => "Greeting";
    public string Description => "Ask for a name with a 20 s timeout (bookmark suspend/resume)";

    public void Register(IModule elsa) => elsa.AddWorkflow<GreetingWorkflow>();

    public async Task RunAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var runner = services.GetRequiredService<IWorkflowRunner>();

        // First run — workflow executes until the AskInput bookmark and suspends.
        var result = await runner.RunAsync<GreetingWorkflow>(cancellationToken: ct);
        var state = result.WorkflowState;

        if (state.Bookmarks.Count == 0) return;

        // Race: user input vs. 20-second timeout.
        var readTask = Task.Run(Console.ReadLine, ct);
        var timedOut = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(20), ct)) != readTask;

        Dictionary<string, object> resumeInput;
        if (timedOut)
        {
            Console.WriteLine();
            Console.WriteLine("[No response in 20 s — resuming with timeout]");
            resumeInput = new Dictionary<string, object> { ["TimedOut"] = true };
        }
        else
        {
            resumeInput = new Dictionary<string, object> { ["UserInput"] = await readTask ?? string.Empty };
        }

        var bookmark = state.Bookmarks.First();
        await runner.RunAsync(result.Workflow, state, new RunWorkflowOptions
        {
            BookmarkId = bookmark.Id,
            Input = resumeInput
        }, ct);
    }
}
