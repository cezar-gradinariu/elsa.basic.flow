using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Basic.Flow.Services.Fulfilment.Workflows;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Messages;
using Elsa.Workflows.Runtime.Requests;

namespace Elsa.Basic.Flow.Services.Fulfilment.Commands;

public class CreateFulfilmentHandler(
    IFulfilmentRepository repository,
    IWorkflowRuntime      workflowRuntime,
    IWorkflowDispatcher   workflowDispatcher)
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

        // Create the workflow instance while the request scope is still alive.
        var client = await workflowRuntime.CreateClientAsync(cancellationToken: ct);
        await client.CreateInstanceAsync(new CreateWorkflowInstanceRequest
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

        // Dispatch to the background worker — Elsa runs the workflow in its own scope,
        // safely outliving the HTTP request's DI scope without Task.Run fire-and-forget.
        await workflowDispatcher.DispatchAsync(
            new DispatchWorkflowInstanceRequest { InstanceId = client.WorkflowInstanceId },
            ct);
    }
}
