namespace Elsa.Basic.Flow.Services.Fulfilment;

public interface IPreparationCompletionTracker
{
    Task RegisterPreparations(string workflowInstanceId, List<string> preparationIds);

    /// <summary>
    /// Marks a single preparation as completed.
    /// Returns (allCompleted, workflowInstanceId) where workflowInstanceId is null if the
    /// preparation was not found in the tracker.
    /// </summary>
    Task<(bool AllCompleted, string? WorkflowInstanceId)> MarkCompletedAsync(string preparationId);

    Task Cleanup(string workflowInstanceId);
}

/// <summary>
/// Development / single-node fallback. Lost on restart — use MongoPreparationCompletionTracker in production.
/// </summary>
public class InMemoryPreparationCompletionTracker : IPreparationCompletionTracker
{
    private readonly Dictionary<string, WorkflowPreparationStatus> _workflowStatus = new();
    private readonly Dictionary<string, string>                    _preparationToWorkflow = new();
    private readonly object _lock = new();

    public Task RegisterPreparations(string workflowInstanceId, List<string> preparationIds)
    {
        lock (_lock)
        {
            _workflowStatus[workflowInstanceId] = new WorkflowPreparationStatus(preparationIds);
            foreach (var prepId in preparationIds)
                _preparationToWorkflow[prepId] = workflowInstanceId;

            Console.WriteLine($"[PreparationTracker] Registered {preparationIds.Count} preparations for workflow {workflowInstanceId}");
        }
        return Task.CompletedTask;
    }

    public Task<(bool AllCompleted, string? WorkflowInstanceId)> MarkCompletedAsync(string preparationId)
    {
        lock (_lock)
        {
            if (!_preparationToWorkflow.TryGetValue(preparationId, out var workflowInstanceId))
                return Task.FromResult<(bool, string?)>((false, null));

            if (!_workflowStatus.TryGetValue(workflowInstanceId, out var status))
                return Task.FromResult<(bool, string?)>((false, null));

            status.MarkCompleted(preparationId);
            Console.WriteLine($"[PreparationTracker] Marked {preparationId} completed for workflow {workflowInstanceId} ({status.CompletedCount}/{status.TotalCount})");
            return Task.FromResult<(bool, string?)>((status.IsAllCompleted, workflowInstanceId));
        }
    }

    public Task Cleanup(string workflowInstanceId)
    {
        lock (_lock)
        {
            if (_workflowStatus.TryGetValue(workflowInstanceId, out var status))
            {
                foreach (var prepId in status.PreparationIds)
                    _preparationToWorkflow.Remove(prepId);
                _workflowStatus.Remove(workflowInstanceId);
                Console.WriteLine($"[PreparationTracker] Cleaned up tracking for workflow {workflowInstanceId}");
            }
        }
        return Task.CompletedTask;
    }

    private sealed class WorkflowPreparationStatus(List<string> preparationIds)
    {
        public HashSet<string> PreparationIds  { get; } = [..preparationIds];
        public HashSet<string> CompletedIds    { get; } = [];
        public int             TotalCount      => PreparationIds.Count;
        public int             CompletedCount  => CompletedIds.Count;
        public bool            IsAllCompleted  => CompletedIds.Count >= PreparationIds.Count;

        public void MarkCompleted(string preparationId) => CompletedIds.Add(preparationId);
    }
}
