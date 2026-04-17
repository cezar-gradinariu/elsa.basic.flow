using Elsa.Basic.Flow.Api;
using Elsa.Basic.Flow.Domain;
using Elsa.Basic.Flow.Infrastructure;
using Elsa.Basic.Flow.Services;
using Elsa.Basic.Flow.Services.Allocation;
using Elsa.Basic.Flow.Services.Fulfilment;
using Elsa.Basic.Flow.Services.Preparation;
using Elsa.Extensions;
using Elsa.Persistence.MongoDb.Extensions;
using Elsa.Persistence.MongoDb.Modules.Management;
using Elsa.Persistence.MongoDb.Modules.Runtime;
using Elsa.Scheduling;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Stimuli;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

// Must be set before any MongoClient is created.
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

var builder = WebApplication.CreateBuilder(args);

// ── MongoDB (domain) ─────────────────────────────────────────────────────────
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
// MongoDB-backed scheduler — persists Delay timers so they survive restarts.
builder.Services.AddSingleton<SchedulerWakeSignal>();
builder.Services.AddSingleton<MongoWorkflowScheduler>();
builder.Services.AddHostedService<SchedulerPollingService>();

// ── Elsa (with MongoDB persistence) ──────────────────────────────────────────
// Registration order: persistence first, then runtime features that depend on it.
var elsaMongoConnection = builder.Configuration.GetConnectionString("ElsaMongo")!;

builder.Services.AddElsa(elsa =>
{
    elsa.UseMongoDb(elsaMongoConnection);
    elsa.UseWorkflowRuntime(r => r.UseMongoDb(_ => { }));
    elsa.UseWorkflowManagement(m => m.UseMongoDb(_ => { }));
    elsa.UseScheduling(scheduling =>
    {
        // Replace the default in-memory scheduler with the MongoDB-backed one.
        // This makes Delay timers durable — they survive process restarts.
        scheduling.WorkflowScheduler = sp => sp.GetRequiredService<MongoWorkflowScheduler>();
    });
    elsa.AddWorkflow<FulfilmentWorkflow>();
    elsa.AddWorkflow<PreparationWorkflow>();
});

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
    // Intentional failure: fail if current second is divisible by 3
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
    IFulfilmentRepository      repository,
    IStimulusSender            stimulusSender,
    ILogger<Program>           logger,
    CancellationToken          ct) =>
{
    logger.LogInformation("[/api/preparation-outcome] Received outcome for preparation {Id} ({Count} container(s))", payload.Id, payload.Containers.Count);
    foreach (var c in payload.Containers)
        logger.LogInformation("  📦 [{ContainerId}] {ContainerType}  lines={Lines}", c.ContainerId, c.ContainerType, c.AllocatedLines.Count);

    try
    {
        // 1. Update the fulfilment aggregate with the containers from this preparation.
        const int maxRetries = 5;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var aggregate = await repository.LoadAsync(payload.FulfilmentId, ct);
                if (aggregate is null)
                {
                    logger.LogWarning("[/api/preparation-outcome] Aggregate {FulfilmentId} not found — skipping container update", payload.FulfilmentId);
                    break;
                }
                aggregate.UpdateContainers(payload.Containers);
                await repository.SaveAsync(aggregate, ct);
                logger.LogInformation("[/api/preparation-outcome] Aggregate {FulfilmentId} updated with {Count} container(s)", payload.FulfilmentId, payload.Containers.Count);
                break;
            }
            catch (ConcurrencyException) when (attempt < maxRetries)
            {
                var delay = TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt - 1));
                logger.LogWarning("[/api/preparation-outcome] Concurrency conflict (attempt {Attempt}/{Max}), retrying in {Delay}ms", attempt, maxRetries, delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
        }

        // 2. Signal the FulfilmentWorkflow that this specific preparation is done.
        //    WaitForPreparationsActivity tracks the count internally via its bookmarks.
        logger.LogInformation("[/api/preparation-outcome] Signalling FulfilmentWorkflow {InstanceId} — preparation {Id} done", payload.ParentWorkflowInstanceId, payload.Id);

        await stimulusSender.SendAsync(
            "Elsa.Event",
            new EventStimulus($"preparation-completed-{payload.Id}"),
            new StimulusMetadata { WorkflowInstanceId = payload.ParentWorkflowInstanceId },
            ct);

        logger.LogInformation("[/api/preparation-outcome] Signal sent");
        return Results.Ok();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[/api/preparation-outcome] Error processing outcome for preparation {Id}", payload.Id);
        return Results.Problem($"Failed to process outcome: {ex.Message}");
    }
});

// Ensure index on elsa_scheduled_tasks.ResumeAt for the polling query.
var elsaDb = app.Services.GetRequiredService<IMongoDatabase>();
await elsaDb.GetCollection<BsonDocument>("elsa_scheduled_tasks")
    .Indexes.CreateOneAsync(
        new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("ResumeAt")));

app.Run();
