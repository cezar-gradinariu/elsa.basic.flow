using Elsa.Basic.Flow.Domain;

namespace Elsa.Basic.Flow.Services.Preparation;

public record PreparationOutcomePayload(Guid Id, Guid FulfilmentId, List<PreparationContainer> Containers);
