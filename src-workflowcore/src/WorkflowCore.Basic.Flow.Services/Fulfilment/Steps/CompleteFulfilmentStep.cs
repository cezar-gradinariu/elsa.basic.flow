using Microsoft.Extensions.Logging;
using WorkflowCore.Basic.Flow.Domain;
using WorkflowCore.Interface;
using WorkflowCore.Models;

namespace WorkflowCore.Basic.Flow.Services.Fulfilment.Steps;

public class CompleteFulfilmentStep(
    IFulfilmentRepository               repository,
    ILogger<CompleteFulfilmentStep>     logger) : IStepBody
{
    public Guid FulfilmentId { get; set; }

    public async Task<ExecutionResult> RunAsync(IStepExecutionContext context)
    {
        var aggregate = await repository.GetByIdAsync(FulfilmentId, context.CancellationToken)
            ?? throw new InvalidOperationException($"Fulfilment {FulfilmentId} not found");

        aggregate.Complete();
        await repository.SaveAsync(aggregate, context.CancellationToken);

        logger.LogInformation("[FulfilmentWorkflow] Fulfilment {Id} completed", FulfilmentId);
        return ExecutionResult.Next();
    }
}
