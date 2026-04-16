using Elsa.Basic.Flow.Domain;

namespace Elsa.Basic.Flow.Services;

public record PrepareCommand(string StoreId, List<OrderLine> Lines);
