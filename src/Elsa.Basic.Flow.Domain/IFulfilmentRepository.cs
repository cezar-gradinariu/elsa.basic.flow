namespace Elsa.Basic.Flow.Domain;

public interface IFulfilmentRepository
{
    Task SaveAsync(FulfilmentAggregate aggregate, CancellationToken ct = default);
}
