using Elsa.Basic.Flow.Domain;

namespace Elsa.Basic.Flow.Services.Preparation.Commands;

public record CreatePreparationCommand(Guid PrepCommandId, Guid FulfilmentId, string StoreId, List<OrderLine> Lines);
