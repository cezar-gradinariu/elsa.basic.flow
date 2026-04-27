using WorkflowCore.Basic.Flow.Domain;

namespace WorkflowCore.Basic.Flow.Api;

public record CreatePreparationRequest(
    Guid            PrepCommandId,
    Guid            FulfilmentId,
    string          StoreId,
    List<OrderLine> Lines);
