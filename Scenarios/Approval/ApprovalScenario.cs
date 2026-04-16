using Elsa.Extensions;
using Elsa.Features.Services;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Messages;
using Elsa.Workflows.Runtime.Stimuli;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Basic.Flow.Scenarios.Approval;

/// <summary>
/// Demonstrates the Fork + Event pattern: the workflow suspends waiting for one
/// of two named external signals, whichever arrives first wins.
/// </summary>
public class ApprovalScenario : IWorkflowScenario
{
    public string Name => "Approval";
    public string Description => "Approve or reject a request via competing external signals (Fork + Event)";

    public void Register(IModule elsa) => elsa.AddWorkflow<ApprovalWorkflow>();

    public async Task RunAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var runtime = services.GetRequiredService<IWorkflowRuntime>();
        var stimulusSender = services.GetRequiredService<IStimulusSender>();

        // Start — workflow runs until it suspends at the Fork/Event pair.
        var client = await runtime.CreateClientAsync(ct);
        await client.CreateAndRunInstanceAsync(new CreateAndRunWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(nameof(ApprovalWorkflow))
        }, ct);

        var instanceId = client.WorkflowInstanceId;

        // Simulate an external system firing one of the two signals.
        Console.WriteLine();
        Console.WriteLine("Simulate an external signal:");
        Console.WriteLine("  [A] Approve");
        Console.WriteLine("  [R] Reject");
        Console.Write("Your choice: ");

        var input = (Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
        var eventName = input.StartsWith("a") ? "signal.approved" : "signal.rejected";
        Console.WriteLine($"[Sending '{eventName}' to workflow {instanceId}]");

        await stimulusSender.SendAsync(
            "Elsa.Event",
            new EventStimulus(eventName),
            new StimulusMetadata { WorkflowInstanceId = instanceId },
            ct);
    }
}
