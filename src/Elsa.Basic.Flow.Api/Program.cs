using Elsa.Basic.Flow.Api;
using Elsa.Basic.Flow.Domain;
using Elsa.Basic.Flow.Infrastructure;
using Elsa.Basic.Flow.Services.Allocation;
using Elsa.Basic.Flow.Services.Fulfilment;
using Elsa.Basic.Flow.Services.Preparation;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using MongoDB.Bson;

// Must be set before any MongoClient is created.
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

var builder = WebApplication.CreateBuilder(args);

// ── MongoDB (domain) ──────────────────────────────────────────────────────────
var mongoSettings = builder.Configuration.GetSection("MongoDB");
var mongoClient   = new MongoClient(mongoSettings["ConnectionString"]);

builder.Services.AddSingleton<IMongoClient>(_ => mongoClient);
builder.Services.AddKeyedScoped<IMongoDatabase>("domain", (_, _) =>
    mongoClient.GetDatabase(mongoSettings["Database"]));

// ── OpenAPI / Swagger ─────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

// ── HTTP client (used by workflow activities) ─────────────────────────────────
builder.Services.AddHttpClient();

// ── Domain / Infrastructure ───────────────────────────────────────────────────
builder.Services.AddScoped<IFulfilmentRepository, MongoFulfilmentRepository>();

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<CreateFulfilmentHandler>();
builder.Services.AddScoped<CreateAllocationHandler>();

// ── Workflow engine + all Elsa wiring (infrastructure detail) ─────────────────
builder.Services.AddWorkflowInfrastructure(builder.Configuration);

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

app.MapOpenApi();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "Elsa Basic Flow API"));
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.MapPost("/api/fulfilments", async (
    CreateFulfilmentRequest request,
    CreateFulfilmentHandler handler,
    CancellationToken ct) =>
{
    var fulfilmentId = Guid.Parse(request.FulfilmentId);
    var cmd = new CreateFulfilmentCommand(
        FulfilmentId: fulfilmentId,
        OrderNo:      request.OrderId,
        StoreNo:      "STORE-DEFAULT",
        CustomerId:   request.CustomerId,
        OrderLines:   request.Lines);

    await handler.HandleAsync(cmd, ct);
    return Results.Created($"/api/fulfilments/{fulfilmentId}", new { Id = fulfilmentId });
});

app.MapGet("/api/fulfilments/{id:guid}", async (
    Guid id,
    IFulfilmentRepository repository,
    CancellationToken ct) =>
{
    var aggregate = await repository.GetByIdAsync(id, ct);
    return aggregate is null ? Results.NotFound() : Results.Ok(aggregate);
});

app.MapPost("/api/allocations", (
    CreateAllocationCommand cmd,
    CreateAllocationHandler handler) =>
{
    if (DateTime.UtcNow.Second % 3 == 0)
    {
        return Results.Problem(
            title:      "Allocation Service Temporarily Unavailable",
            detail:     "Service is experiencing temporary issues. Please try again.",
            statusCode: 503);
    }

    var result = handler.Handle(cmd);
    return Results.Ok(result);
});

app.MapPost("/api/preparation-outcome", async (
    PreparationOutcomePayload  payload,
    IPreparationOutcomeHandler handler,
    CancellationToken          ct) =>
{
    await handler.HandleAsync(payload, ct);
    return Results.Ok();
});

app.Run();
