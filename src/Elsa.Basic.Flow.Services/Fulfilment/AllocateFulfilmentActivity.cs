using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Basic.Flow.Services.Allocation;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Services.Fulfilment;

internal class AllocateFulfilmentActivity : Activity
{
    [Output] public Output<AllocationResult> Result { get; set; } = new();

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var input = context.WorkflowExecutionContext.Input;

        object? orderNoVal = null, storeIdVal = null, orderLinesVal = null;
        input?.TryGetValue("OrderNo",     out orderNoVal);
        input?.TryGetValue("StoreId",     out storeIdVal);
        input?.TryGetValue("OrderLines",  out orderLinesVal);

        var orderNo    = orderNoVal?.ToString()    ?? string.Empty;
        var storeId    = storeIdVal?.ToString()    ?? string.Empty;
        var orderLines = JsonSerializer.Deserialize<List<OrderLine>>(
                             orderLinesVal?.ToString() ?? "[]")
                         ?? [];

        var cmd    = new CreateAllocationCommand(orderNo, storeId, orderLines);
        var result = new CreateAllocationHandler().Handle(cmd);

        context.Set(Result, result);
        await context.CompleteActivityAsync();
    }
}
