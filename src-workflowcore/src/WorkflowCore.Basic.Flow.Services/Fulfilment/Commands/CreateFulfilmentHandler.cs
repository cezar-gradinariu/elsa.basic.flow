using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkflowCore.Basic.Flow.Domain;
using WorkflowCore.Basic.Flow.Services.Fulfilment.Workflows;
using WorkflowCore.Interface;

namespace WorkflowCore.Basic.Flow.Services.Fulfilment.Commands;

public class CreateFulfilmentHandler(
    IFulfilmentRepository               repository,
    IWorkflowHost                       workflowHost,
    ILogger<CreateFulfilmentHandler>    logger)
{
    public async Task HandleAsync(CreateFulfilmentCommand cmd, CancellationToken ct = default)
    {
        var aggregate = FulfilmentAggregate.Create(
            cmd.FulfilmentId, cmd.OrderNo, cmd.StoreNo, cmd.CustomerId, cmd.OrderLines);
        await repository.SaveAsync(aggregate, ct);

        var data = new FulfilmentWorkflowData
        {
            FulfilmentId = cmd.FulfilmentId,
            OrderNo      = cmd.OrderNo,
            StoreId      = cmd.StoreNo,
            CustomerId   = cmd.CustomerId,
            LinesJson    = JsonSerializer.Serialize(cmd.OrderLines)
        };

        await workflowHost.StartWorkflow("FulfilmentWorkflow", 1, data);
        logger.LogInformation("[CreateFulfilment] Workflow started for {FulfilmentId}", cmd.FulfilmentId);
    }
}
