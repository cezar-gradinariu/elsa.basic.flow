using WorkflowCore.Basic.Flow.Domain;

namespace WorkflowCore.Basic.Flow.Services.Fulfilment.Commands;

public record ApplyContainersCommand(Guid FulfilmentId, List<PreparationContainer> Containers);
