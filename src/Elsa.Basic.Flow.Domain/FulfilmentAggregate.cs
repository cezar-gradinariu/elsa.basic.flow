namespace Elsa.Basic.Flow.Domain;

public class FulfilmentAggregate
{
    public Fulfilment       Root   { get; private set; } = null!;
    public FulfilmentStatus Status { get; private set; }

    private FulfilmentAggregate() { }

    public static FulfilmentAggregate Create(
        Guid   fulfilmentId,
        string orderNo,
        string storeNo,
        string customerId,
        List<OrderLine> orderLines)
    {
        return new FulfilmentAggregate
        {
            Root   = new Fulfilment(fulfilmentId, orderNo, storeNo, customerId, orderLines),
            Status = FulfilmentStatus.Created
        };
    }
}
