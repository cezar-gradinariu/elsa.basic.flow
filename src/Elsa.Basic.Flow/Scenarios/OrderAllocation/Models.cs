namespace Elsa.Basic.Flow.Scenarios.OrderAllocation;

public record OrderLine(string ArticleId, string Name, int Quantity);
public record Order(string Id, List<OrderLine> Lines);
public record AllocatedLine(string ArticleId, string Name, int Quantity, int StoreId);
public record StoreGroup(int StoreId, List<AllocatedLine> Lines);
