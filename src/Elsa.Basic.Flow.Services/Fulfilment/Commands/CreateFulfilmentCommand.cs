using Elsa.Basic.Flow.Domain;

namespace Elsa.Basic.Flow.Services.Fulfilment;

public record CreateFulfilmentCommand(
    Guid   FulfilmentId,
    string OrderNo,
    string StoreNo,
    string CustomerId,
    List<OrderLine> OrderLines);
