using WorkflowCore.Basic.Flow.Domain;

namespace WorkflowCore.Basic.Flow.Services.Fulfilment.Commands;

public record CreateFulfilmentCommand(
    Guid            FulfilmentId,
    string          OrderNo,
    string          StoreNo,
    string          CustomerId,
    List<OrderLine> OrderLines);
