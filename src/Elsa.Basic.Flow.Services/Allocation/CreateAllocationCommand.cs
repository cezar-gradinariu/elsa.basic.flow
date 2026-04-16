using Elsa.Basic.Flow.Domain;

namespace Elsa.Basic.Flow.Services.Allocation;

public record CreateAllocationCommand(string OrderNo, string StoreId, List<OrderLine> OrderLines);
