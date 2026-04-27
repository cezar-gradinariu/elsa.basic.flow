using WorkflowCore.Basic.Flow.Domain;

namespace WorkflowCore.Basic.Flow.Services.Allocation;

public record CreateAllocationCommand(string OrderNo, string StoreId, List<OrderLine> OrderLines);
