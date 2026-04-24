using Elsa.Basic.Flow.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Elsa.Basic.Flow.Infrastructure;

public class MongoFulfilmentRepository(
    [FromKeyedServices("domain")] IMongoDatabase db,
    ILogger<MongoFulfilmentRepository> logger) : IFulfilmentRepository
{
    private IMongoCollection<FulfilmentDocument> Col =>
        db.GetCollection<FulfilmentDocument>("fulfilments");

    public async Task SaveAsync(FulfilmentAggregate aggregate, CancellationToken ct = default)
    {
        var doc = new FulfilmentDocument
        {
            Id         = aggregate.FulfilmentId,
            OrderNo    = aggregate.OrderNo,
            StoreNo    = aggregate.StoreNo,
            CustomerId = aggregate.CustomerId,
            Status     = aggregate.Status.ToString(),
            Version    = aggregate.Version,
            OrderLines = (aggregate.OrderLines ?? [])
                .Select(l => new FulfilmentLineDocument
                {
                    OrderNo       = l.OrderNo,
                    Sku           = l.Sku,
                    Quantity      = l.Quantity,
                    UnitOfMeasure = l.UnitOfMeasure
                })
                .ToList(),
            Containers = (aggregate.Containers ?? [])
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
                        })
                        .ToList()
                })
                .ToList()
        };

        if (aggregate.Version == 0)
        {
            try
            {
                logger.LogDebug("Inserting aggregate {Id}", aggregate.FulfilmentId);
                await Col.InsertOneAsync(doc, cancellationToken: ct);
                logger.LogInformation("Inserted aggregate {Id} v{Version}", aggregate.FulfilmentId, aggregate.Version);
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                logger.LogWarning("Duplicate key conflict for aggregate {Id}", aggregate.FulfilmentId);
                throw new ConcurrencyException(aggregate.FulfilmentId, aggregate.Version);
            }
        }
        else
        {
            var filter = Builders<FulfilmentDocument>.Filter.And(
                Builders<FulfilmentDocument>.Filter.Eq(x => x.Id,      doc.Id),
                Builders<FulfilmentDocument>.Filter.Eq(x => x.Version, aggregate.Version - 1));

            // Use $set rather than ReplaceOneAsync so that fields managed outside the aggregate
            // (e.g. PrepCompletedCount, which is incremented atomically by IncrementPrepCompletedAsync)
            // are never overwritten with default values.
            var update = Builders<FulfilmentDocument>.Update
                .Set(x => x.OrderNo,    doc.OrderNo)
                .Set(x => x.StoreNo,    doc.StoreNo)
                .Set(x => x.CustomerId, doc.CustomerId)
                .Set(x => x.Status,     doc.Status)
                .Set(x => x.Version,    doc.Version)
                .Set(x => x.OrderLines, doc.OrderLines)
                .Set(x => x.Containers, doc.Containers);

            logger.LogDebug("Updating aggregate {Id}: v{Old} → v{New}", aggregate.FulfilmentId, aggregate.Version - 1, aggregate.Version);
            var result = await Col.UpdateOneAsync(filter, update, cancellationToken: ct);

            if (result.MatchedCount == 0)
            {
                logger.LogWarning("Concurrency conflict on aggregate {Id}: expected v{Version} was already superseded", aggregate.FulfilmentId, aggregate.Version - 1);
                throw new ConcurrencyException(aggregate.FulfilmentId, aggregate.Version - 1);
            }

            logger.LogInformation("Updated aggregate {Id} to v{Version}", aggregate.FulfilmentId, aggregate.Version);
        }
    }

    public async Task<FulfilmentAggregate?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await Col
            .Find(x => x.Id == id)
            .FirstOrDefaultAsync(ct);

        if (doc is null) return null;

        return FulfilmentAggregate.Reconstitute(
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

    public async Task<FulfilmentAggregate?> LoadAsync(Guid id, CancellationToken ct = default)
    {
        logger.LogDebug("Loading aggregate {Id}", id);
        var doc = await Col
            .Find(x => x.Id == id)
            .FirstOrDefaultAsync(ct);

        if (doc is null)
        {
            logger.LogWarning("Aggregate {Id} not found", id);
            return null;
        }

        logger.LogDebug("Loaded aggregate {Id} v{Version}", id, doc.Version);

        return FulfilmentAggregate.Reconstitute(
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

    public async Task<int> IncrementPrepCompletedAsync(Guid id, CancellationToken ct = default)
    {
        // Uses MongoDB $inc for an atomic server-side increment.
        // Safe to call concurrently from multiple workflow callbacks — no read-modify-write race.
        var filter = Builders<FulfilmentDocument>.Filter.Eq(x => x.Id, id);
        var update = Builders<FulfilmentDocument>.Update.Inc(x => x.PrepCompletedCount, 1);
        var options = new FindOneAndUpdateOptions<FulfilmentDocument>
        {
            ReturnDocument = ReturnDocument.After,
            Projection     = Builders<FulfilmentDocument>.Projection.Include(x => x.PrepCompletedCount)
        };

        var updated = await Col.FindOneAndUpdateAsync(filter, update, options, ct);
        return updated?.PrepCompletedCount ?? 0;
    }
}
