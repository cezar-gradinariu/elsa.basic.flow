using Elsa.Basic.Flow.Domain;

namespace Elsa.Basic.Flow.Services.Fulfilment.Commands;

public record ApplyContainersCommand(Guid FulfilmentId, List<PreparationContainer> Containers);
