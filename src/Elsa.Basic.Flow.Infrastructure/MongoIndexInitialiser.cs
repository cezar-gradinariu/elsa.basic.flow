using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Elsa.Basic.Flow.Infrastructure;

public class MongoIndexInitialiser(IMongoDatabase elsaDb) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await elsaDb
            .GetCollection<BsonDocument>("elsa_scheduled_tasks")
            .Indexes.CreateOneAsync(
                new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys.Ascending("ResumeAt")),
                cancellationToken: ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
