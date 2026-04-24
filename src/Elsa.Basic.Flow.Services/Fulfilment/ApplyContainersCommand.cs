using Elsa.Basic.Flow.Domain;

namespace Elsa.Basic.Flow.Services.Fulfilment;

public record ApplyContainersCommand(Guid FulfilmentId, List<PreparationContainer> Containers);
