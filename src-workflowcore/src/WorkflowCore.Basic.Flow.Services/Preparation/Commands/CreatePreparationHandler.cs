using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkflowCore.Basic.Flow.Services.Preparation.Workflows;
using WorkflowCore.Interface;

namespace WorkflowCore.Basic.Flow.Services.Preparation.Commands;

public class CreatePreparationHandler(
    IWorkflowHost                       workflowHost,
    ILogger<CreatePreparationHandler>   logger)
{
    public async Task HandleAsync(CreatePreparationCommand cmd, CancellationToken ct = default)
    {
        var data = new PreparationWorkflowData
        {
            PrepCommandId = cmd.PrepCommandId,
            FulfilmentId  = cmd.FulfilmentId,
            StoreId       = cmd.StoreId,
            LinesJson     = JsonSerializer.Serialize(cmd.Lines)
        };

        await workflowHost.StartWorkflow("PreparationWorkflow", 1, data);
        logger.LogInformation("[CreatePreparation] Workflow started for {PrepCommandId}", cmd.PrepCommandId);
    }
}
