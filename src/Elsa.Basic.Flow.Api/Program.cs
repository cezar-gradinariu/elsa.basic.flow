using Elsa.Basic.Flow.Domain;
using Elsa.Basic.Flow.Infrastructure;
using Elsa.Basic.Flow.Services;
using Elsa.Extensions;
using Elsa.Persistence.MongoDb.Extensions;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Messages;
using Elsa.Workflows.Models;
using MongoDB.Driver;
using System.Text.Json;
using Elsa.Basic.Flow.Services.Allocation;
using Elsa.Basic.Flow.Services.Fulfilment;
using Elsa.Basic.Flow.Services.Preparation;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// ── MongoDB (domain) ─────────────────────────────────────────────────────────
var mongoSettings = builder.Configuration.GetSection("MongoDB");
var mongoClient   = new MongoClient(mongoSettings["ConnectionString"]);

builder.Services.AddSingleton<IMongoClient>(_ => mongoClient);
builder.Services.AddScoped<IMongoDatabase>(_ =>
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

// ── Elsa (with MongoDB persistence) ──────────────────────────────────────────
var elsaMongoConnection = builder.Configuration.GetConnectionString("ElsaMongo")!;

builder.Services.AddElsa(elsa =>
{
    elsa.UseWorkflowRuntime();
    elsa.UseMongoDb(elsaMongoConnection);
    elsa.UseScheduling(); // Enable scheduling for Delay activities
    elsa.AddWorkflow<FulfilmentWorkflow>();
    elsa.AddWorkflow<PreparationWorkflow>();
    elsa.UseWorkflowManagement();
});

// Configure application to handle graceful shutdown better
builder.Services.Configure<HostOptions>(opts => 
{
    opts.ShutdownTimeout = TimeSpan.FromSeconds(2); // Shorter timeout to reduce disposal window
});

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

app.MapOpenApi();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "Elsa Basic Flow API"));
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.MapPost("/api/fulfilments", async (
    CreateFulfilmentCommand cmd,
    CreateFulfilmentHandler handler,
    CancellationToken ct) =>
{
    await handler.HandleAsync(cmd, ct);
    return Results.Created($"/api/fulfilments/{cmd.FulfilmentId}", null);
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
    var result = handler.Handle(cmd);
    return Results.Ok(result);
});

app.MapPost("/api/preparation/prepare", async (
    PrepareCommand cmd,
    IWorkflowRuntime workflowRuntime,
    CancellationToken ct) =>
{
    var client = await workflowRuntime.CreateClientAsync(cancellationToken: ct);

    var instanceRequest = new CreateWorkflowInstanceRequest
    {
        WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(nameof(PreparationWorkflow)),
        CorrelationId            = $"preparation-{cmd.Id}",
        Input = new Dictionary<string, object>
        {
            ["PrepareCommandId"] = cmd.Id.ToString(),
            ["StoreId"]          = cmd.StoreId,
            ["Lines"]            = JsonSerializer.Serialize(cmd.Lines)
        }
    };

    // Create and start the workflow instance - Elsa will handle background execution
    await client.CreateInstanceAsync(instanceRequest, ct);
    await client.RunInstanceAsync(RunWorkflowInstanceRequest.Empty, ct);

    return Results.Ok(new { cmd.Id });
});

app.MapPost("/api/preparation-outcome", (PreparationOutcomePayload payload) =>
{
    Console.WriteLine();
    Console.WriteLine($"[/api/preparation-outcome] Received outcome for preparation {payload.Id}  ({payload.Containers.Count} container(s)):");
    foreach (var c in payload.Containers)
    {
        Console.WriteLine($"  [{c.ContainerId}] {c.ContainerType}  lines={c.AllocatedLines.Count}");
        foreach (var l in c.AllocatedLines)
            Console.WriteLine($"      {l.OrderLineNo,-20} articleId:{l.ArticleId,-20} qty:{l.Quantity,3}");
    }
    return Results.Ok();
});

app.Run();
