using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Microsoft.Extensions.Logging;
using Elsa.Workflows;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Services.Preparation;

internal class LogPreparationRequestActivity : Activity
{
    public Output<string>?   PrepareCommandIdOut      { get; set; }
    public Output<string>?   LinesJsonOut             { get; set; }
    public Output<string>?   FulfilmentIdOut          { get; set; }
    public Output<string>?   ParentWorkflowInstanceIdOut { get; set; }
    /// <summary>Per-instance random delay duration, computed here so the Delay activity
    /// receives a variable value rather than one baked in at workflow registration time.</summary>
    public Output<TimeSpan>? DelayOut                 { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var input = context.WorkflowExecutionContext.Input;

        object? idVal = null, storeIdVal = null, linesVal = null, fulfilmentIdVal = null, parentInstanceIdVal = null;
        input?.TryGetValue("PrepareCommandId",        out idVal);
        input?.TryGetValue("StoreId",                 out storeIdVal);
        input?.TryGetValue("Lines",                   out linesVal);
        input?.TryGetValue("FulfilmentId",            out fulfilmentIdVal);
        input?.TryGetValue("ParentWorkflowInstanceId", out parentInstanceIdVal);

        var id               = idVal?.ToString()             ?? "(unknown)";
        var storeId          = storeIdVal?.ToString()        ?? "(unknown)";
        var fulfilmentId     = fulfilmentIdVal?.ToString()   ?? "(unknown)";
        var parentInstanceId = parentInstanceIdVal?.ToString() ?? "(unknown)";

        var linesJson = linesVal?.ToString() ?? "[]";
        var lines     = JsonSerializer.Deserialize<List<OrderLine>>(linesJson) ?? [];

        // Compute per-instance delay here (execution time), not in Build() (registration time).
        var randomSeconds = Random.Shared.Next(5, 31);
        var delay         = TimeSpan.FromSeconds(randomSeconds);

        context.Set(PrepareCommandIdOut,         id);
        context.Set(LinesJsonOut,                linesJson);
        context.Set(FulfilmentIdOut,             fulfilmentId);
        context.Set(ParentWorkflowInstanceIdOut, parentInstanceId);
        context.Set(DelayOut,                    delay);

        var logger = context.GetRequiredService<ILogger<LogPreparationRequestActivity>>();
        logger.LogInformation("[PreparationWorkflow] id={Id}  store={StoreId}  lines={LineCount}  delay={Delay}s  fulfilmentId={FulfilmentId}",
            id, storeId, lines.Count, randomSeconds, fulfilmentId);

        if (lines.Count == 0)
            logger.LogWarning("[PreparationWorkflow] No order lines for preparation {Id} — will produce 0 containers", id);

        await context.CompleteActivityAsync();
    }
}
