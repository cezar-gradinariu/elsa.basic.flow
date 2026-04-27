using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace WorkflowCore.Basic.Flow.Infrastructure;

internal class FulfilmentDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid   Id                 { get; set; }
    public string OrderNo            { get; set; } = "";
    public string StoreNo            { get; set; } = "";
    public string CustomerId         { get; set; } = "";
    public string Status             { get; set; } = "";
    public int    Version            { get; set; }
    public int    PrepCompletedCount { get; set; }
    public List<FulfilmentLineDocument> OrderLines  { get; set; } = [];
    public List<ContainerDocument>      Containers  { get; set; } = [];
}

internal class FulfilmentLineDocument
{
    public string OrderNo       { get; set; } = "";
    public string Sku           { get; set; } = "";
    public int    Quantity      { get; set; }
    public string UnitOfMeasure { get; set; } = "";
}

internal class ContainerDocument
{
    public string ContainerId   { get; set; } = "";
    public string ContainerType { get; set; } = "";
    public List<AllocatedLineDocument> AllocatedLines { get; set; } = [];
}

internal class AllocatedLineDocument
{
    public string OrderLineNo { get; set; } = "";
    public string ArticleId   { get; set; } = "";
    public int    Quantity    { get; set; }
}
