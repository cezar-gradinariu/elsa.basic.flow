namespace Elsa.Basic.Flow.Domain;

public class FulfilmentAggregate
{
    public Guid                         FulfilmentId { get; private set; }
    public string                       OrderNo      { get; private set; } = "";
    public string                       StoreNo      { get; private set; } = "";
    public string                       CustomerId   { get; private set; } = "";
    public List<OrderLine>              OrderLines   { get; private set; } = [];
    public List<PreparationContainer>   Containers   { get; private set; } = [];
    public FulfilmentStatus             Status       { get; private set; }
    public int                          Version      { get; private set; }

    private FulfilmentAggregate() { }

    public static FulfilmentAggregate Reconstitute(
        Guid                        fulfilmentId,
        string                      orderNo,
        string                      storeNo,
        string                      customerId,
        List<OrderLine>             orderLines,
        List<PreparationContainer>  containers,
        FulfilmentStatus            status,
        int                         version)
    {
        return new FulfilmentAggregate
        {
            FulfilmentId = fulfilmentId,
            OrderNo      = orderNo,
            StoreNo      = storeNo,
            CustomerId   = customerId,
            OrderLines   = orderLines,
            Containers   = containers,
            Status       = status,
            Version      = version
        };
    }

    public static FulfilmentAggregate Create(
        Guid            fulfilmentId,
        string          orderNo,
        string          storeNo,
        string          customerId,
        List<OrderLine> orderLines)
    {
        return new FulfilmentAggregate
        {
            FulfilmentId = fulfilmentId,
            OrderNo      = orderNo,
            StoreNo      = storeNo,
            CustomerId   = customerId,
            OrderLines   = orderLines,
            Containers   = [],
            Status       = FulfilmentStatus.Created,
            Version      = 0
        };
    }

    public void UpdateContainers(List<PreparationContainer> containers)
    {
        Containers.AddRange(containers);
        Version++;
    }
}
