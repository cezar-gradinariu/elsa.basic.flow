using WorkflowCore.Basic.Flow.Services.Preparation.Steps;
using WorkflowCore.Interface;
using WorkflowCore.Models;

namespace WorkflowCore.Basic.Flow.Services.Preparation.Workflows;

public class PreparationWorkflow : IWorkflow<PreparationWorkflowData>
{
    public string Id      => "PreparationWorkflow";
    public int    Version => 1;

    public void Build(IWorkflowBuilder<PreparationWorkflowData> builder)
    {
        builder
            .StartWith<LogPreparationStep>()
                .Input(step => step.PrepCommandId, data => data.PrepCommandId)
                .Input(step => step.StoreId,       data => data.StoreId)
                .Input(step => step.LinesJson,     data => data.LinesJson)
                .Output(data => data.DelaySeconds,   step => step.DelaySeconds)
                .Output(data => data.ContainersJson, step => step.ContainersJson)
            .Delay(data => TimeSpan.FromSeconds(data.DelaySeconds))
            .Then<SendPreparationOutcomeStep>()
                .Input(step => step.PrepCommandId,  data => data.PrepCommandId)
                .Input(step => step.FulfilmentId,   data => data.FulfilmentId)
                .Input(step => step.ContainersJson, data => data.ContainersJson)
                .OnError(WorkflowErrorHandling.Retry, TimeSpan.FromSeconds(2));
    }
}
