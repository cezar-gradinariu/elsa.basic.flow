namespace Elsa.Basic.Flow.Services.Preparation;

public record PreparationContainer(
    string              ContainerId,
    ContainerType       ContainerType,
    List<AllocatedLine> AllocatedLines);
