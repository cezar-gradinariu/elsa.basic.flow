namespace Elsa.Basic.Flow.Domain;

public interface IFulfilmentRepository
{
    Task SaveAsync(FulfilmentAggregate aggregate, CancellationToken ct = default);
    Task<FulfilmentAggregate?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<FulfilmentAggregate?> LoadAsync(Guid id, CancellationToken ct = default);
    /// <summary>
    /// Atomically increments the preparation completed counter and returns the new value.
    /// Safe to call concurrently — uses MongoDB $inc, not read-modify-write.
    /// </summary>
    Task<int> IncrementPrepCompletedAsync(Guid id, CancellationToken ct = default);
}
