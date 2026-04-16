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
            Id         = aggregate.Root.FulfilmentId,
            OrderNo    = aggregate.Root.OrderNo,
            StoreNo    = aggregate.Root.StoreNo,
            CustomerId = aggregate.Root.CustomerId,
            Status     = aggregate.Status.ToString(),
            OrderLines = aggregate.Root.OrderLines
                .Select(l => new FulfilmentLineDocument
                {
                    OrderNo       = l.OrderNo,
                    Sku           = l.Sku,
                    Quantity      = l.Quantity,
                    UnitOfMeasure = l.UnitOfMeasure
                })
                .ToList()
        };

        await Col.ReplaceOneAsync(
            x => x.Id == doc.Id,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct);
    }
}
