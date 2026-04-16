using Elsa.Basic.Flow.Domain;

namespace Elsa.Basic.Flow.Services.Allocation;

public record StoreAllocation(string StoreId, List<OrderLine> Lines);
