using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkflowCore.Basic.Flow.Domain;
using WorkflowCore.Basic.Flow.Services.Fulfilment.Commands;
using WorkflowCore.Interface;
using WorkflowCore.Models;

namespace WorkflowCore.Basic.Flow.Services.Fulfilment.Steps;

public class IncrementPreparationCountStep(
    IFulfilmentRepository                   repository,
    ApplyContainersHandler                  applyContainersHandler,
    ILogger<IncrementPreparationCountStep>  logger) : IStepBody
{
    // Inputs
    public Guid   FulfilmentId  { get; set; }
    public string ContainersJson { get; set; } = "[]";

    // Output
    public int CompletedCount { get; set; }

    public async Task<ExecutionResult> RunAsync(IStepExecutionContext context)
    {
        var containers = JsonSerializer.Deserialize<List<PreparationContainer>>(ContainersJson) ?? [];

        if (containers.Count > 0)
            await applyContainersHandler.HandleAsync(new ApplyContainersCommand(FulfilmentId, containers), context.CancellationToken);

        CompletedCount = await repository.IncrementPrepCompletedAsync(FulfilmentId, context.CancellationToken);

        logger.LogInformation("[FulfilmentWorkflow] Preparation completed ({Completed} so far)", CompletedCount);
        return ExecutionResult.Next();
    }
}
