namespace Elsa.Basic.Flow.Services.Fulfilment;

public interface IPreparationCompletionTracker
{
    Task RegisterPreparations(string workflowInstanceId, List<string> preparationIds);
    Task<bool> MarkCompleted(string preparationId, out string? workflowInstanceId);
    Task<bool> AreAllCompleted(string workflowInstanceId);
    Task Cleanup(string workflowInstanceId);
}

public class InMemoryPreparationCompletionTracker : IPreparationCompletionTracker
{
    private readonly Dictionary<string, WorkflowPreparationStatus> _workflowStatus = new();
    private readonly Dictionary<string, string> _preparationToWorkflow = new();
    private readonly object _lock = new();

    public Task RegisterPreparations(string workflowInstanceId, List<string> preparationIds)
    {
        lock (_lock)
        {
            _workflowStatus[workflowInstanceId] = new WorkflowPreparationStatus(preparationIds);
            
            foreach (var prepId in preparationIds)
            {
                _preparationToWorkflow[prepId] = workflowInstanceId;
            }
            
            Console.WriteLine($"[PreparationTracker] Registered {preparationIds.Count} preparations for workflow {workflowInstanceId}");
        }
        
        return Task.CompletedTask;
    }

    public Task<bool> MarkCompleted(string preparationId, out string? workflowInstanceId)
    {
        workflowInstanceId = null;
        
        lock (_lock)
        {
            if (!_preparationToWorkflow.TryGetValue(preparationId, out workflowInstanceId))
            {
                return Task.FromResult(false);
            }

            if (_workflowStatus.TryGetValue(workflowInstanceId, out var status))
            {
                status.MarkCompleted(preparationId);
                Console.WriteLine($"[PreparationTracker] Marked preparation {preparationId} completed for workflow {workflowInstanceId} ({status.CompletedCount}/{status.TotalCount})");
                return Task.FromResult(status.IsAllCompleted);
            }
        }
        
        return Task.FromResult(false);
    }

    public Task<bool> AreAllCompleted(string workflowInstanceId)
    {
        lock (_lock)
        {
            return Task.FromResult(
                _workflowStatus.TryGetValue(workflowInstanceId, out var status) && 
                status.IsAllCompleted
            );
        }
    }

    public Task Cleanup(string workflowInstanceId)
    {
        lock (_lock)
        {
            if (_workflowStatus.TryGetValue(workflowInstanceId, out var status))
            {
                foreach (var prepId in status.PreparationIds)
                {
                    _preparationToWorkflow.Remove(prepId);
                }
                _workflowStatus.Remove(workflowInstanceId);
                Console.WriteLine($"[PreparationTracker] Cleaned up tracking for workflow {workflowInstanceId}");
            }
        }
        
        return Task.CompletedTask;
    }

    private class WorkflowPreparationStatus
    {
        public HashSet<string> PreparationIds { get; }
        public HashSet<string> CompletedIds { get; } = new();
        
        public int TotalCount => PreparationIds.Count;
        public int CompletedCount => CompletedIds.Count;
        public bool IsAllCompleted => CompletedIds.Count >= PreparationIds.Count;

        public WorkflowPreparationStatus(List<string> preparationIds)
        {
            PreparationIds = new HashSet<string>(preparationIds);
        }

        public void MarkCompleted(string preparationId)
        {
            CompletedIds.Add(preparationId);
        }
    }
}