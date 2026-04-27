namespace WorkflowCore.Basic.Flow.Services.Fulfilment.Workflows;

public class FulfilmentWorkflowData
{
    // Inputs — set when the workflow is started
    public Guid   FulfilmentId { get; set; }
    public string OrderNo      { get; set; } = "";
    public string StoreId      { get; set; } = "";
    public string CustomerId   { get; set; } = "";
    public string LinesJson    { get; set; } = "[]";

    // Written by AllocateFulfilmentStep
    public string AllocationJson { get; set; } = "";

    // Written by SendPrepareCommandsStep
    public string PrepareCommandsJson { get; set; } = "[]";
    public int    TotalPreparations   { get; set; }

    // Maintained by the WaitFor loop
    public int    CompletedPreparations  { get; set; }
    public string LatestContainersJson   { get; set; } = "[]";
}
