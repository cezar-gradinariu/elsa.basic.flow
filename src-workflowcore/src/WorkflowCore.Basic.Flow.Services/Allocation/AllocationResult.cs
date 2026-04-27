namespace WorkflowCore.Basic.Flow.Services.Allocation;

public record AllocationResult(string OrderNo, List<StoreAllocation> StoreAllocations);
