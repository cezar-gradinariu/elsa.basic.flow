namespace Elsa.Basic.Flow.Domain;

public record OrderLine(
    string OrderNo,
    string Sku,
    int    Quantity,
    string UnitOfMeasure);
