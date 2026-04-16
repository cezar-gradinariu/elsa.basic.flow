namespace Elsa.Basic.Flow.Domain;

public record PreparationContainer(
    string              ContainerId,
    ContainerType       ContainerType,
    List<AllocatedLine> AllocatedLines);

public record AllocatedLine(string OrderLineNo, string ArticleId, int Quantity);

public enum ContainerType { Bag, Tote }