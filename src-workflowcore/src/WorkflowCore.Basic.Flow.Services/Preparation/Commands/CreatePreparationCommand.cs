using WorkflowCore.Basic.Flow.Domain;

namespace WorkflowCore.Basic.Flow.Services.Preparation.Commands;

public record CreatePreparationCommand(
    Guid            PrepCommandId,
    Guid            FulfilmentId,
    string          StoreId,
    List<OrderLine> Lines);
