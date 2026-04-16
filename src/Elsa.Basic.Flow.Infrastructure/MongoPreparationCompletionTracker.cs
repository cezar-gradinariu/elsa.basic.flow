using Elsa.Basic.Flow.Services.Fulfilment;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Elsa.Basic.Flow.Infrastructure;

/// <summary>
/// MongoDB-backed implementation of IPreparationCompletionTracker.
/// State survives process restarts, making it safe for use with persisted Elsa workflows.
/// </summary>
public class MongoPreparationCompletionTracker([FromKeyedServices("domain")] IMongoDatabase db) : IPreparationCompletionTracker
{
    private IMongoCollection<PrepTrackDocument> Col =>
        db.GetCollection<PrepTrackDocument>("prep_tracking");

    public async Task RegisterPreparations(string workflowInstanceId, List<string> preparationIds)
    {
        var doc = new PrepTrackDocument
        {
            WorkflowInstanceId   = workflowInstanceId,
            AllPreparationIds    = preparationIds,
            CompletedPreparationIds = []
        };
        await Col.InsertOneAsync(doc);
        Console.WriteLine($"[MongoTracker] Registered {preparationIds.Count} preparations for workflow {workflowInstanceId}");
    }

    public async Task<(bool AllCompleted, string? WorkflowInstanceId)> MarkCompletedAsync(string preparationId)
    {
        var filter = Builders<PrepTrackDocument>.Filter.AnyEq(d => d.AllPreparationIds, preparationId);
        var update = Builders<PrepTrackDocument>.Update.AddToSet(d => d.CompletedPreparationIds, preparationId);
        var options = new FindOneAndUpdateOptions<PrepTrackDocument> { ReturnDocument = ReturnDocument.After };

        var doc = await Col.FindOneAndUpdateAsync(filter, update, options);
        if (doc is null)
        {
            Console.WriteLine($"[MongoTracker] Preparation {preparationId} not found in any tracked workflow");
            return (false, null);
        }

        var allCompleted = doc.CompletedPreparationIds.Count >= doc.AllPreparationIds.Count;
        Console.WriteLine($"[MongoTracker] Marked {preparationId} completed for workflow {doc.WorkflowInstanceId} ({doc.CompletedPreparationIds.Count}/{doc.AllPreparationIds.Count})");
        return (allCompleted, doc.WorkflowInstanceId);
    }

    public async Task Cleanup(string workflowInstanceId)
    {
        await Col.DeleteOneAsync(d => d.WorkflowInstanceId == workflowInstanceId);
        Console.WriteLine($"[MongoTracker] Cleaned up tracking for workflow {workflowInstanceId}");
    }
}

[BsonIgnoreExtraElements]
internal sealed class PrepTrackDocument
{
    [BsonId]
    public string       WorkflowInstanceId      { get; set; } = "";
    public List<string> AllPreparationIds       { get; set; } = [];
    public List<string> CompletedPreparationIds { get; set; } = [];
}
