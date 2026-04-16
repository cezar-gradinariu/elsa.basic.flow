namespace Elsa.Basic.Flow.Domain;

public record Fulfilment(
    Guid   FulfilmentId,
    string OrderNo,
    string StoreNo,
    string CustomerId,
    List<OrderLine> OrderLines);
