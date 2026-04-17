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
        if (_sem.CurrentCount == 0)
            _sem.Release();
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct)
        => _sem.WaitAsync(timeout, ct);
}
