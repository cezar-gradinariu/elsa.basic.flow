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

            logger.LogDebug("Updating aggregate {Id}: v{Old} → v{New}", aggregate.FulfilmentId, aggregate.Version - 1, aggregate.Version);
            var result = await Col.ReplaceOneAsync(filter, doc, cancellationToken: ct);

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
                Enum.Parse<ContainerType>(c.ContainerType),
                (c.AllocatedLines ?? []).Select(l => new AllocatedLine(l.OrderLineNo, l.ArticleId, l.Quantity)).ToList()
            )).ToList(),
            Enum.Parse<FulfilmentStatus>(doc.Status),
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
                Enum.Parse<ContainerType>(c.ContainerType),
                (c.AllocatedLines ?? []).Select(l => new AllocatedLine(l.OrderLineNo, l.ArticleId, l.Quantity)).ToList()
            )).ToList(),
            Enum.Parse<FulfilmentStatus>(doc.Status),
            doc.Version);
    }
}
