using Elsa.Workflows;

namespace Elsa.Basic.Flow.Services.Preparation.Activities;

/// <summary>
/// First activity in StorePreparationSubWorkflow. Copies workflow Input into Properties
/// so values survive process restarts (Input is empty on restart; Properties persist in MongoDB).
/// </summary>
internal class InitialiseStorePreparationPropertiesActivity : Activity
{
    internal const string PrepCommandIdKey = "_sp_prep_command_id";
    internal const string FulfilmentIdKey  = "_sp_fulfilment_id";
    internal const string StoreIdKey       = "_sp_store_id";
    internal const string LinesKey         = "_sp_lines";

    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var props = context.WorkflowExecutionContext.Properties;
        var input = context.WorkflowExecutionContext.Input;

        object? prepId = null, fid = null, sid = null, lines = null;
        input?.TryGetValue("PrepCommandId", out prepId);
        input?.TryGetValue("FulfilmentId",  out fid);
        input?.TryGetValue("StoreId",       out sid);
        input?.TryGetValue("Lines",         out lines);

        props[PrepCommandIdKey] = prepId?.ToString() ?? Guid.Empty.ToString();
        props[FulfilmentIdKey]  = fid?.ToString()    ?? string.Empty;
        props[StoreIdKey]       = sid?.ToString()    ?? string.Empty;
        props[LinesKey]         = lines?.ToString()  ?? "[]";

        return context.CompleteActivityAsync();
    }
}
