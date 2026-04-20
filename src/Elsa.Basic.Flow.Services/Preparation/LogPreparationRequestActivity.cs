using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Microsoft.Extensions.Logging;
using Elsa.Workflows;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Services.Preparation;

internal class LogPreparationRequestActivity : Activity
{
    public Output<string>?   PrepareCommandIdOut { get; set; }
    public Output<string>?   LinesJsonOut        { get; set; }
    public Output<TimeSpan>? DelayOut            { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var input = context.WorkflowExecutionContext.Input;

        object? idVal = null, storeIdVal = null, linesVal = null;
        input?.TryGetValue("PrepareCommandId", out idVal);
        input?.TryGetValue("StoreId",          out storeIdVal);
        input?.TryGetValue("Lines",            out linesVal);

        var id      = idVal?.ToString()      ?? "(unknown)";
        var storeId = storeIdVal?.ToString() ?? "(unknown)";

        var linesJson = linesVal?.ToString() ?? "[]";
        var lines     = JsonSerializer.Deserialize<List<OrderLine>>(linesJson) ?? [];

        var randomSeconds = Random.Shared.Next(5, 31);
        var delay         = TimeSpan.FromSeconds(randomSeconds);

        context.Set(PrepareCommandIdOut, id);
        context.Set(LinesJsonOut,        linesJson);
        context.Set(DelayOut,            delay);

        var logger = context.GetRequiredService<ILogger<LogPreparationRequestActivity>>();
        logger.LogInformation("[PreparationWorkflow] id={Id}  store={StoreId}  lines={LineCount}  delay={Delay}s",
            id, storeId, lines.Count, randomSeconds);

        if (lines.Count == 0)
            logger.LogWarning("[PreparationWorkflow] No order lines for preparation {Id} — will produce 0 containers", id);

        await context.CompleteActivityAsync();
    }
}
