using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Basic.Flow.Services.Fulfilment;

public class CreateFulfilmentHandler(
    IFulfilmentRepository repository,
    IWorkflowRuntime      workflowRuntime,
    IServiceScopeFactory  scopeFactory)
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

        // Create and persist the workflow instance while the request scope is still alive.
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

        // Run in a fresh scope so the workflow outlives the HTTP request's DI scope.
        var instanceId = client.WorkflowInstanceId;
        _ = Task.Run(async () =>
        {
            await using var scope  = scopeFactory.CreateAsyncScope();
            var runtime            = scope.ServiceProvider.GetRequiredService<IWorkflowRuntime>();
            var backgroundClient   = await runtime.CreateClientAsync(instanceId, CancellationToken.None);
            await backgroundClient.RunInstanceAsync(RunWorkflowInstanceRequest.Empty, CancellationToken.None);
        });
    }
}
