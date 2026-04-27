using WorkflowCore.Basic.Flow.Domain;

namespace WorkflowCore.Basic.Flow.Api;

public record CreateFulfilmentRequest(
    string          FulfilmentId,
    string          CustomerId,
    string          OrderId,
    List<OrderLine> Lines);
