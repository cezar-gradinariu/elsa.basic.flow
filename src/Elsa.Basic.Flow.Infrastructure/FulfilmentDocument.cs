using MongoDB.Bson.Serialization.Attributes;

namespace Elsa.Basic.Flow.Infrastructure;

internal class FulfilmentDocument
{
    [BsonId]
    public Guid   Id         { get; set; }
    public string OrderNo    { get; set; } = "";
    public string StoreNo    { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string Status     { get; set; } = "";
    public List<FulfilmentLineDocument> OrderLines { get; set; } = [];
}

internal class FulfilmentLineDocument
{
    public string OrderNo       { get; set; } = "";
    public string Sku           { get; set; } = "";
    public int    Quantity      { get; set; }
    public string UnitOfMeasure { get; set; } = "";
}
