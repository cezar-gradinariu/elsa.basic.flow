using Elsa.Workflows;
using Elsa.Workflows.Activities;

namespace Elsa.Basic.Flow.Services;

public class FulfilmentWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Root = new Sequence
        {
            Activities =
            [
                new LogFulfilmentCreated()
            ]
        };
    }
}

/// <summary>
/// Reads fulfilment details from workflow input and logs them.
/// Input keys: FulfilmentId, OrderNo, StoreNo.
/// </summary>
internal class LogFulfilmentCreated : Activity
{
    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var input = context.WorkflowExecutionContext.Input;

        object? fid = null, ord = null, sto = null;
        input?.TryGetValue("FulfilmentId", out fid);
        input?.TryGetValue("OrderNo",      out ord);
        input?.TryGetValue("StoreNo",      out sto);

        var fulfilmentId = fid?.ToString() ?? "unknown";
        var orderNo      = ord?.ToString() ?? "unknown";
        var storeNo      = sto?.ToString() ?? "unknown";

        Console.WriteLine(
            $"[Workflow] Fulfilment {fulfilmentId} created — order {orderNo}, store {storeNo}");

        return ValueTask.CompletedTask;
    }
}
