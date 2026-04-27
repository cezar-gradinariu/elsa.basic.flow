using WorkflowCore.Basic.Flow.Domain;

namespace WorkflowCore.Basic.Flow.Services.Preparation.Commands;

public record PrepareCommand(Guid Id, string StoreId, List<OrderLine> Lines);
