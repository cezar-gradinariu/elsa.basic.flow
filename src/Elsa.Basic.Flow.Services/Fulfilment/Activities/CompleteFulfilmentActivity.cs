using Elsa.Basic.Flow.Domain;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Basic.Flow.Services.Fulfilment;

internal class CompleteFulfilmentActivity : Activity
{
    public Input<string>? FulfilmentId { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var fulfilmentId = Guid.Parse(context.Get(FulfilmentId)
            ?? throw new InvalidOperationException("FulfilmentId is required"));

        var repository = context.GetRequiredService<IFulfilmentRepository>();
        var logger     = context.GetRequiredService<ILogger<CompleteFulfilmentActivity>>();

        var aggregate = await repository.GetByIdAsync(fulfilmentId, context.CancellationToken)
            ?? throw new InvalidOperationException($"Fulfilment {fulfilmentId} not found");

        aggregate.Complete();
        await repository.SaveAsync(aggregate, context.CancellationToken);

        logger.LogInformation("[FulfilmentWorkflow] Fulfilment {FulfilmentId} completed — all preparation workflows finished", fulfilmentId);

        await context.CompleteActivityAsync();
    }
}
