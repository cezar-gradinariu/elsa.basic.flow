using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using WorkflowCore.Basic.Flow.Api;
using WorkflowCore.Basic.Flow.Domain;
using WorkflowCore.Basic.Flow.Infrastructure;
using WorkflowCore.Basic.Flow.Services.Allocation;
using WorkflowCore.Basic.Flow.Services.Fulfilment;
using WorkflowCore.Basic.Flow.Services.Fulfilment.Commands;
using WorkflowCore.Basic.Flow.Services.Fulfilment.Workflows;
using WorkflowCore.Basic.Flow.Services.Preparation;
using WorkflowCore.Basic.Flow.Services.Preparation.Commands;
using WorkflowCore.Basic.Flow.Services.Preparation.Workflows;
using WorkflowCore.Interface;

// Must be set before any MongoClient is created.
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

var builder = WebApplication.CreateBuilder(args);

// ── MongoDB ───────────────────────────────────────────────────────────────────
var mongoSettings = builder.Configuration.GetSection("MongoDB");
var mongoClient   = new MongoClient(mongoSettings["ConnectionString"]);

builder.Services.AddSingleton<IMongoClient>(_ => mongoClient);

// ── OpenAPI ───────────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

// ── HTTP client (used by workflow steps) ──────────────────────────────────────
builder.Services.AddHttpClient();

// ── Workflow engine + infrastructure ─────────────────────────────────────────
builder.Services.AddWorkflowInfrastructure(mongoClient, builder.Configuration);

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

app.MapOpenApi();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "WorkflowCore Basic Flow API"));
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

// ── Endpoints ─────────────────────────────────────────────────────────────────

app.MapPost("/api/fulfilments", async (
    CreateFulfilmentRequest request,
    CreateFulfilmentHandler handler,
    CancellationToken       ct) =>
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
    Guid                  id,
    IFulfilmentRepository repository,
    CancellationToken     ct) =>
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

app.MapPost("/api/preparations", async (
    CreatePreparationRequest request,
    CreatePreparationHandler handler,
    CancellationToken        ct) =>
{
    var cmd = new CreatePreparationCommand(
        PrepCommandId: request.PrepCommandId,
        FulfilmentId:  request.FulfilmentId,
        StoreId:       request.StoreId,
        Lines:         request.Lines);

    await handler.HandleAsync(cmd, ct);
    return Results.Accepted();
});

app.MapPost("/api/preparation-outcome", async (
    PreparationOutcomePayload payload,
    IWorkflowHost             workflowHost,
    CancellationToken         ct) =>
{
    var containersJson = JsonSerializer.Serialize(payload.Containers);
    await workflowHost.PublishEvent(
        "PreparationCompleted",
        payload.FulfilmentId.ToString(),
        containersJson);

    return Results.Ok();
});

// ── Workflow host start/stop ──────────────────────────────────────────────────
var workflowHost = app.Services.GetRequiredService<IWorkflowHost>();
workflowHost.RegisterWorkflow<FulfilmentWorkflow, FulfilmentWorkflowData>();
workflowHost.RegisterWorkflow<PreparationWorkflow, PreparationWorkflowData>();
workflowHost.Start();

app.Lifetime.ApplicationStopping.Register(() => workflowHost.Stop());

app.Run();
