using System.Text.Json;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Messages;
using Elsa.Workflows.Runtime.Requests;

namespace Elsa.Basic.Flow.Services.Preparation;

public class CreatePreparationHandler(
    IWorkflowRuntime     workflowRuntime,
    IWorkflowDispatcher  workflowDispatcher)
{
    public async Task HandleAsync(CreatePreparationCommand cmd, CancellationToken ct = default)
    {
        var client = await workflowRuntime.CreateClientAsync(cancellationToken: ct);

        await client.CreateInstanceAsync(new CreateWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(nameof(PreparationWorkflow)),
            CorrelationId            = $"preparation-{cmd.PrepCommandId}",
            Input = new Dictionary<string, object>
            {
                ["PrepareCommandId"] = cmd.PrepCommandId.ToString(),
                ["StoreId"]          = cmd.StoreId,
                ["Lines"]            = JsonSerializer.Serialize(cmd.Lines)
            }
        }, ct);

        await workflowDispatcher.DispatchAsync(
            new DispatchWorkflowInstanceRequest { InstanceId = client.WorkflowInstanceId },
            ct);
    }
}
