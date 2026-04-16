namespace Elsa.Basic.Flow.Services;

public record AllocationResult(string OrderNo, List<StoreAllocation> StoreAllocations);
