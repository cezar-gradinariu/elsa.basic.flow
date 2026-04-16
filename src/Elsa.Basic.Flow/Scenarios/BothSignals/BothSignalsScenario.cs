using Elsa.Extensions;
using Elsa.Features.Services;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Messages;
using Elsa.Workflows.Runtime.Stimuli;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Basic.Flow.Scenarios.BothSignals;

/// <summary>
/// Demonstrates Fork WaitAll: sends two signals in sequence, workflow only
/// completes after both 'signal.alpha' and 'signal.beta' are received.
/// </summary>
public class BothSignalsScenario : IWorkflowScenario
{
    private static readonly string[] AllSignals = ["signal.alpha", "signal.beta"];
    public string Name => "Both Signals";
    public string Description => "Proceed only after both Alpha and Beta signals arrive (Fork WaitAll)";

    public void Register(IModule elsa) => elsa.AddWorkflow<BothSignalsWorkflow>();

    public async Task RunAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var runtime = services.GetRequiredService<IWorkflowRuntime>();
        var stimulusSender = services.GetRequiredService<IStimulusSender>();

        // Start — workflow runs until it suspends at both Event activities.
        var client = await runtime.CreateClientAsync(ct);
        await client.CreateAndRunInstanceAsync(new CreateAndRunWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(nameof(BothSignalsWorkflow))
        }, ct);

        var instanceId = client.WorkflowInstanceId;

        // Send two signals in any order — each signal may only be sent once.
        var sent = new HashSet<string>();
        while (sent.Count < 2)
        {
            var remaining = AllSignals.Except(sent).ToList();

            Console.WriteLine();
            Console.WriteLine($"Send signal ({sent.Count + 1}/2) — remaining: {string.Join(", ", remaining)}");
            Console.WriteLine("  [A] signal.alpha");
            Console.WriteLine("  [B] signal.beta");
            Console.Write("Your choice: ");

            var input = (Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
            var eventName = input.StartsWith("a") ? "signal.alpha" : "signal.beta";

            if (!sent.Add(eventName))
            {
                Console.WriteLine($"['{eventName}' was already sent — pick the other one]");
                continue;
            }

            Console.WriteLine($"[Sending '{eventName}' to workflow {instanceId}]");
            await stimulusSender.SendAsync(
                "Elsa.Event",
                new EventStimulus(eventName),
                new StimulusMetadata { WorkflowInstanceId = instanceId },
                ct);
        }
    }
}
