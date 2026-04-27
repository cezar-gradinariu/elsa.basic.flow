namespace WorkflowCore.Basic.Flow.Services.Preparation.Workflows;

public class PreparationWorkflowData
{
    // Inputs — set when the workflow is started
    public Guid   PrepCommandId { get; set; }
    public Guid   FulfilmentId  { get; set; }
    public string StoreId       { get; set; } = "";
    public string LinesJson     { get; set; } = "[]";

    // Written by LogPreparationStep (built once, reused on retries)
    public int    DelaySeconds   { get; set; }
    public string ContainersJson { get; set; } = "[]";

    // Retry counter for SendPreparationOutcomeStep
    public int SendAttempts { get; set; }
}
