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
            Version    = aggregate.Version + 1,
            OrderLines = aggregate.OrderLines
                .Select(l => new FulfilmentLineDocument
                {
                    OrderNo       = l.OrderNo,
                    Sku           = l.Sku,
                    Quantity      = l.Quantity,
                    UnitOfMeasure = l.UnitOfMeasure
                })
                .ToList()
        };

        if (aggregate.Version == 0)
        {
            // New aggregate — insert. A duplicate key means another writer already
            // persisted this fulfilment, which is a concurrency conflict.
            try
            {
                await Col.InsertOneAsync(doc, cancellationToken: ct);
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
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
                Builders<FulfilmentDocument>.Filter.Eq(x => x.Version, aggregate.Version));

            var result = await Col.ReplaceOneAsync(filter, doc, cancellationToken: ct);

            if (result.MatchedCount == 0)
                throw new ConcurrencyException(aggregate.FulfilmentId, aggregate.Version);
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
            doc.OrderLines.Select(l => new OrderLine(l.OrderNo, l.Sku, l.Quantity, l.UnitOfMeasure)).ToList(),
            Enum.Parse<FulfilmentStatus>(doc.Status),
            doc.Version);
    }
}
