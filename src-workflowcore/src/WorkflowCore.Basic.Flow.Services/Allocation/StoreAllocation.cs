using WorkflowCore.Basic.Flow.Domain;

namespace WorkflowCore.Basic.Flow.Services.Allocation;

public record StoreAllocation(string StoreId, List<OrderLine> Lines);
