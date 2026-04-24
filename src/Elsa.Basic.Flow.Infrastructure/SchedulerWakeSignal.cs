namespace Elsa.Basic.Flow.Infrastructure;

/// <summary>
/// Shared signal between MongoWorkflowScheduler and SchedulerPollingService.
/// When a new task is scheduled, the scheduler notifies this signal so the poller
/// wakes up immediately rather than waiting for its current sleep to expire.
/// </summary>
public sealed class SchedulerWakeSignal
{
    private readonly SemaphoreSlim _sem = new(0, 1);

    public void Notify()
    {
        // Try to release the semaphore. Ignore SemaphoreFullException — it means the signal
        // is already pending (another Notify fired first and the poller hasn't consumed it yet).
        // Catching is safer than checking CurrentCount first, which has a TOCTOU race when
        // multiple schedulers notify concurrently.
        try { _sem.Release(); }
        catch (SemaphoreFullException) { }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct)
        => _sem.WaitAsync(timeout, ct);
}
