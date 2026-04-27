using WorkflowCore.Basic.Flow.Services.Fulfilment.Steps;
using WorkflowCore.Interface;
using WorkflowCore.Models;

namespace WorkflowCore.Basic.Flow.Services.Fulfilment.Workflows;

public class FulfilmentWorkflow : IWorkflow<FulfilmentWorkflowData>
{
    public string Id      => "FulfilmentWorkflow";
    public int    Version => 1;

    public void Build(IWorkflowBuilder<FulfilmentWorkflowData> builder)
    {
        builder
            .StartWith<AllocateFulfilmentStep>()
                .Input(step => step.OrderNo,  data => data.OrderNo)
                .Input(step => step.StoreId,  data => data.StoreId)
                .Input(step => step.LinesJson, data => data.LinesJson)
                .Output(data => data.AllocationJson, step => step.AllocationJson)
                .OnError(WorkflowErrorHandling.Retry, TimeSpan.FromSeconds(2))
            
            .Then<SendPrepareCommandsStep>()
                .Input(step => step.FulfilmentId,   data => data.FulfilmentId)
                .Input(step => step.AllocationJson, data => data.AllocationJson)
                .Output(data => data.PrepareCommandsJson, step => step.PrepareCommandsJson)
                .Output(data => data.TotalPreparations,   step => step.TotalPreparations)
                .OnError(WorkflowErrorHandling.Retry, TimeSpan.FromSeconds(2))
            .While(data => data.CompletedPreparations < data.TotalPreparations)
                .Do(inner => inner
                    .WaitFor("PreparationCompleted", (data, ctx) => data.FulfilmentId.ToString())
                        .Output(data => data.LatestContainersJson,
                                step => step.EventData == null ? "[]" : (string)step.EventData)
                    .Then<IncrementPreparationCountStep>()
                        .Input(step => step.FulfilmentId,   data => data.FulfilmentId)
                        .Input(step => step.ContainersJson, data => data.LatestContainersJson)
                        .Output(data => data.CompletedPreparations, step => step.CompletedCount))
            .Then<CompleteFulfilmentStep>()
                .Input(step => step.FulfilmentId, data => data.FulfilmentId);
    }
}
