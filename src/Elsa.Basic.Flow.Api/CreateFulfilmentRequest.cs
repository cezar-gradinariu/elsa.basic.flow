using Elsa.Basic.Flow.Domain;

namespace Elsa.Basic.Flow.Api;

public record CreateFulfilmentRequest(
    string FulfilmentId,
    string CustomerId,
    string OrderId,
    List<OrderLine> Lines);