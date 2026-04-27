namespace WorkflowCore.Basic.Flow.Domain;

public interface IFulfilmentRepository
{
    Task SaveAsync(FulfilmentAggregate aggregate, CancellationToken ct = default);
    Task<FulfilmentAggregate?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<FulfilmentAggregate?> LoadAsync(Guid id, CancellationToken ct = default);
    /// <summary>Atomically increments the prep-completed counter and returns the new value.</summary>
    Task<int> IncrementPrepCompletedAsync(Guid id, CancellationToken ct = default);
}
