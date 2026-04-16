using Elsa.Basic.Flow.Domain;

namespace Elsa.Basic.Flow.Services.Preparation;

public record PrepareCommand(Guid Id, string StoreId, List<OrderLine> Lines, string FulfilmentId);
