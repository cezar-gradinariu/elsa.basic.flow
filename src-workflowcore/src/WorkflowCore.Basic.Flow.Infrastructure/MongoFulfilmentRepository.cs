using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using WorkflowCore.Basic.Flow.Domain;

namespace WorkflowCore.Basic.Flow.Infrastructure;

public class MongoFulfilmentRepository(
    IMongoDatabase                     db,
    ILogger<MongoFulfilmentRepository> logger) : IFulfilmentRepository
{
    private IMongoCollection<FulfilmentDocument> Col =>
        db.GetCollection<FulfilmentDocument>("fulfilments");

    public async Task SaveAsync(FulfilmentAggregate aggregate, CancellationToken ct = default)
    {
        var doc = ToDocument(aggregate);

        if (aggregate.Version == 0)
        {
            try
            {
                await Col.InsertOneAsync(doc, cancellationToken: ct);
                logger.LogInformation("Inserted aggregate {Id} v{Version}", aggregate.FulfilmentId, aggregate.Version);
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                throw new ConcurrencyException(aggregate.FulfilmentId, aggregate.Version);
            }
        }
        else
        {
            var filter = Builders<FulfilmentDocument>.Filter.And(
                Builders<FulfilmentDocument>.Filter.Eq(x => x.Id,      doc.Id),
                Builders<FulfilmentDocument>.Filter.Eq(x => x.Version, aggregate.Version - 1));

            var update = Builders<FulfilmentDocument>.Update
                .Set(x => x.OrderNo,    doc.OrderNo)
                .Set(x => x.StoreNo,    doc.StoreNo)
                .Set(x => x.CustomerId, doc.CustomerId)
                .Set(x => x.Status,     doc.Status)
                .Set(x => x.Version,    doc.Version)
                .Set(x => x.OrderLines, doc.OrderLines)
                .Set(x => x.Containers, doc.Containers);

            var result = await Col.UpdateOneAsync(filter, update, cancellationToken: ct);

            if (result.MatchedCount == 0)
            {
                logger.LogWarning("Concurrency conflict on {Id}: expected v{Version}", aggregate.FulfilmentId, aggregate.Version - 1);
                throw new ConcurrencyException(aggregate.FulfilmentId, aggregate.Version - 1);
            }

            logger.LogInformation("Updated aggregate {Id} to v{Version}", aggregate.FulfilmentId, aggregate.Version);
        }
    }

    public async Task<FulfilmentAggregate?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await Col.Find(x => x.Id == id).FirstOrDefaultAsync(ct);
        return doc is null ? null : FromDocument(doc);
    }

    public async Task<FulfilmentAggregate?> LoadAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await Col.Find(x => x.Id == id).FirstOrDefaultAsync(ct);
        return doc is null ? null : FromDocument(doc);
    }

    public async Task<int> IncrementPrepCompletedAsync(Guid id, CancellationToken ct = default)
    {
        var filter  = Builders<FulfilmentDocument>.Filter.Eq(x => x.Id, id);
        var update  = Builders<FulfilmentDocument>.Update.Inc(x => x.PrepCompletedCount, 1);
        var options = new FindOneAndUpdateOptions<FulfilmentDocument>
        {
            ReturnDocument = ReturnDocument.After,
            Projection     = Builders<FulfilmentDocument>.Projection.Include(x => x.PrepCompletedCount)
        };

        var updated = await Col.FindOneAndUpdateAsync(filter, update, options, ct);
        return updated?.PrepCompletedCount ?? 0;
    }

    private static FulfilmentDocument ToDocument(FulfilmentAggregate a) =>
        new()
        {
            Id         = a.FulfilmentId,
            OrderNo    = a.OrderNo,
            StoreNo    = a.StoreNo,
            CustomerId = a.CustomerId,
            Status     = a.Status.ToString(),
            Version    = a.Version,
            OrderLines = (a.OrderLines ?? [])
                .Select(l => new FulfilmentLineDocument
                {
                    OrderNo       = l.OrderNo,
                    Sku           = l.Sku,
                    Quantity      = l.Quantity,
                    UnitOfMeasure = l.UnitOfMeasure
                }).ToList(),
            Containers = (a.Containers ?? [])
                .Select(c => new ContainerDocument
                {
                    ContainerId   = c.ContainerId,
                    ContainerType = c.ContainerType.ToString(),
                    AllocatedLines = (c.AllocatedLines ?? [])
                        .Select(l => new AllocatedLineDocument
                        {
                            OrderLineNo = l.OrderLineNo,
                            ArticleId   = l.ArticleId,
                            Quantity    = l.Quantity
                        }).ToList()
                }).ToList()
        };

    private static FulfilmentAggregate FromDocument(FulfilmentDocument doc) =>
        FulfilmentAggregate.Reconstitute(
            doc.Id,
            doc.OrderNo,
            doc.StoreNo,
            doc.CustomerId,
            (doc.OrderLines ?? []).Select(l => new OrderLine(l.OrderNo, l.Sku, l.Quantity, l.UnitOfMeasure)).ToList(),
            (doc.Containers ?? []).Select(c => new PreparationContainer(
                c.ContainerId,
                Enum.TryParse<ContainerType>(c.ContainerType, out var ct2) ? ct2 : ContainerType.Tote,
                (c.AllocatedLines ?? []).Select(l => new AllocatedLine(l.OrderLineNo, l.ArticleId, l.Quantity)).ToList()
            )).ToList(),
            Enum.TryParse<FulfilmentStatus>(doc.Status, out var status) ? status : FulfilmentStatus.Created,
            doc.Version);
}
