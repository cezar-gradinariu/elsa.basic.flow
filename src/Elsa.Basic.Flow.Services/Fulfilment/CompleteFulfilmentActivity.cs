using Elsa.Workflows;
using Elsa.Workflows.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Basic.Flow.Services.Fulfilment;

internal class CompleteFulfilmentActivity : Activity
{
    public Input<string>? FulfilmentId { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var fulfilmentId = context.Get(FulfilmentId) ?? "unknown";

        var logger = context.GetRequiredService<ILogger<CompleteFulfilmentActivity>>();
        logger.LogInformation("[FulfilmentWorkflow] Fulfilment {FulfilmentId} completed — all preparation workflows finished", fulfilmentId);

        await context.CompleteActivityAsync();
    }
}
