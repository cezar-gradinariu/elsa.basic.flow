using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Basic.Flow.Services.Allocation;
using Elsa.Workflows;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Services.Fulfilment;

internal class AllocateFulfilmentActivity : Activity
{
    public Output<AllocationResult>? Result       { get; set; }
    public Output<string>?          FulfilmentId { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var input = context.WorkflowExecutionContext.Input;

        object? fulfilmentIdVal = null, orderNoVal = null, storeIdVal = null, orderLinesVal = null;
        input?.TryGetValue("FulfilmentId", out fulfilmentIdVal);
        input?.TryGetValue("OrderNo",      out orderNoVal);
        input?.TryGetValue("StoreId",      out storeIdVal);
        input?.TryGetValue("OrderLines",   out orderLinesVal);

        context.Set(FulfilmentId, fulfilmentIdVal?.ToString() ?? "unknown");

        var orderNo    = orderNoVal?.ToString()   ?? string.Empty;
        var storeId    = storeIdVal?.ToString()   ?? string.Empty;
        var orderLines = JsonSerializer.Deserialize<List<OrderLine>>(orderLinesVal?.ToString() ?? "[]") ?? [];

        var cmd     = new CreateAllocationCommand(orderNo, storeId, orderLines);
        var handler = context.GetRequiredService<CreateAllocationHandler>();
        var result  = handler.Handle(cmd);

        context.Set(Result, result);
        await context.CompleteActivityAsync();
    }
}
