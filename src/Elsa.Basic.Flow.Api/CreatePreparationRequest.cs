using Elsa.Basic.Flow.Domain;

namespace Elsa.Basic.Flow.Api;

public record CreatePreparationRequest(Guid PrepCommandId, Guid FulfilmentId, string StoreId, List<OrderLine> Lines);
