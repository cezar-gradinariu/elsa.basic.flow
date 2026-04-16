using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Messages;

namespace Elsa.Basic.Flow.Services;

public class CreateFulfilmentHandler(
    IFulfilmentRepository repository,
    IWorkflowRuntime      workflowRuntime)
{
    public async Task HandleAsync(CreateFulfilmentCommand cmd, CancellationToken ct)
    {
        var aggregate = FulfilmentAggregate.Create(
            cmd.FulfilmentId,
            cmd.OrderNo,
            cmd.StoreNo,
            cmd.CustomerId,
            cmd.OrderLines);

        await repository.SaveAsync(aggregate, ct);

        var client = await workflowRuntime.CreateClientAsync(cancellationToken: ct);

        await client.CreateAndRunInstanceAsync(new CreateAndRunWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(nameof(FulfilmentWorkflow)),
            CorrelationId            = $"fulfilment-{cmd.FulfilmentId}",
            Input = new Dictionary<string, object>
            {
                ["FulfilmentId"] = cmd.FulfilmentId.ToString(),
                ["OrderNo"]      = cmd.OrderNo,
                ["StoreId"]      = cmd.StoreNo,
                ["OrderLines"]   = JsonSerializer.Serialize(cmd.OrderLines)
            }
        }, ct);
    }
}
