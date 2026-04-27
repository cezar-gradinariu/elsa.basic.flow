using WorkflowCore.Basic.Flow.Domain;

namespace WorkflowCore.Basic.Flow.Services.Preparation;

public record PreparationOutcomePayload(
    Guid                       PrepCommandId,
    Guid                       FulfilmentId,
    List<PreparationContainer> Containers);
