using Elsa.Basic.Flow.Domain;
using MongoDB.Driver;

namespace Elsa.Basic.Flow.Infrastructure;

public class MongoFulfilmentRepository(IMongoDatabase db) : IFulfilmentRepository
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
            Version    = aggregate.Version, // ← Remove the +1 since UpdateContainers already incremented it
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
            // New aggregate — insert. A duplicate key means another writer already
            // persisted this fulfilment, which is a concurrency conflict.
            try
            {
                Console.WriteLine($"[MongoRepository] Inserting new aggregate {aggregate.FulfilmentId} (Version: {aggregate.Version})");
                await Col.InsertOneAsync(doc, cancellationToken: ct);
                Console.WriteLine($"[MongoRepository] ✓ Inserted aggregate {aggregate.FulfilmentId} (Version: {aggregate.Version})");
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                Console.WriteLine($"[MongoRepository] ✗ Duplicate key conflict for {aggregate.FulfilmentId}");
                throw new ConcurrencyException(aggregate.FulfilmentId, aggregate.Version);
            }
        }
        else
        {
            // Existing aggregate — replace only if the version in the DB still matches.
            // No upsert: if the filter misses, the version was already advanced by a
            // concurrent writer and we must not silently overwrite their changes.
            var filter = Builders<FulfilmentDocument>.Filter.And(
                Builders<FulfilmentDocument>.Filter.Eq(x => x.Id,      doc.Id),
                Builders<FulfilmentDocument>.Filter.Eq(x => x.Version, aggregate.Version - 1)); // Look for the old version

            Console.WriteLine($"[MongoRepository] Updating aggregate {aggregate.FulfilmentId}: {aggregate.Version - 1} → {aggregate.Version}");
            var result = await Col.ReplaceOneAsync(filter, doc, cancellationToken: ct);

            if (result.MatchedCount == 0)
            {
                Console.WriteLine($"[MongoRepository] ✗ Concurrency conflict: expected version {aggregate.Version - 1} was already superseded");
                throw new ConcurrencyException(aggregate.FulfilmentId, aggregate.Version - 1);
            }
            
            Console.WriteLine($"[MongoRepository] ✓ Updated aggregate {aggregate.FulfilmentId} to version {aggregate.Version}");
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
        Console.WriteLine($"[MongoRepository] Loading aggregate {id}...");
        var doc = await Col
            .Find(x => x.Id == id)
            .FirstOrDefaultAsync(ct);

        if (doc is null) 
        {
            Console.WriteLine($"[MongoRepository] ✗ Aggregate {id} not found");
            return null;
        }

        Console.WriteLine($"[MongoRepository] ✓ Loaded aggregate {id} (Version: {doc.Version})");

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
