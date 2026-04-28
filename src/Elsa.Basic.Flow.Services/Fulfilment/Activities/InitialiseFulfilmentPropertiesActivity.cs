using Elsa.Workflows;

namespace Elsa.Basic.Flow.Services.Fulfilment.Activities;

/// <summary>
/// First activity in FulfilmentWorkflow. Copies workflow Input values into
/// WorkflowExecutionContext.Properties so they survive process restarts.
///
/// Verified: Elsa 3.6 does NOT preserve WorkflowExecutionContext.Input across
/// process restarts — Input is empty when a workflow resumes after a crash.
/// Properties are serialised with the workflow instance document and survive.
/// </summary>
internal class InitialiseFulfilmentPropertiesActivity : Activity
{
    internal const string FulfilmentIdKey = "_alloc_fulfilment_id";
    internal const string OrderNoKey      = "_alloc_order_no";
    internal const string StoreIdKey      = "_alloc_store_id";
    internal const string OrderLinesKey   = "_alloc_order_lines";

    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var props = context.WorkflowExecutionContext.Properties;
        var input = context.WorkflowExecutionContext.Input;

        object? fid = null, ono = null, sid = null, ol = null;
        input?.TryGetValue("FulfilmentId", out fid);
        input?.TryGetValue("OrderNo",      out ono);
        input?.TryGetValue("StoreId",      out sid);
        input?.TryGetValue("OrderLines",   out ol);

        props[FulfilmentIdKey] = fid?.ToString() ?? "unknown";
        props[OrderNoKey]      = ono?.ToString() ?? string.Empty;
        props[StoreIdKey]      = sid?.ToString() ?? string.Empty;
        props[OrderLinesKey]   = ol?.ToString()  ?? "[]";

        return context.CompleteActivityAsync();
    }
}
