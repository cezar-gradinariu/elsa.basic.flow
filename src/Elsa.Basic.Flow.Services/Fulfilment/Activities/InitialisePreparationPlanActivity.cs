using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Basic.Flow.Services.Allocation;
using Elsa.Workflows;
using Elsa.Workflows.Models;

namespace Elsa.Basic.Flow.Services.Fulfilment.Activities;

internal record PreparationPlanItem(Guid PrepCommandId, string StoreId, List<OrderLine> Lines);

/// <summary>
/// Generates a stable dispatch plan (one PrepCommandId per store allocation) and commits it
/// to WorkflowExecutionContext.Properties before any subworkflow is dispatched.
///
/// Splitting plan generation from dispatch ensures that if the process crashes mid-dispatch,
/// DispatchAndWaitPreparationsActivity restarts with the same PrepCommandIds — making
/// correlation IDs stable and preventing silent duplicate-subworkflow fan-in errors.
/// </summary>
internal class InitialisePreparationPlanActivity : Activity
{
    internal const string PlanKey       = "_dpwa_plan";
    internal const string TotalKey      = "_dpwa_total";
    internal const string FulfilmentKey = "_dpwa_fulfilment_id";

    public Input<AllocationResult>? AllocationResult { get; set; }
    public Input<string>?           FulfilmentId     { get; set; }

    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var result       = context.Get(AllocationResult)!;
        var fulfilmentId = context.Get(FulfilmentId) ?? string.Empty;
        var props        = context.WorkflowExecutionContext.Properties;

        var plan = result.StoreAllocations
            .Select(a => new PreparationPlanItem(Guid.NewGuid(), a.StoreId, a.Lines))
            .ToList();

        props[PlanKey]       = JsonSerializer.Serialize(plan);
        props[TotalKey]      = plan.Count;
        props[FulfilmentKey] = fulfilmentId;

        return context.CompleteActivityAsync();
    }
}
