using Elsa.Basic.Flow.Domain;
using Elsa.Basic.Flow.Infrastructure;
using Elsa.Basic.Flow.Services;
using Elsa.Extensions;
using Elsa.Persistence.MongoDb.Extensions;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// ── MongoDB (domain) ─────────────────────────────────────────────────────────
var mongoSettings = builder.Configuration.GetSection("MongoDB");
var mongoClient   = new MongoClient(mongoSettings["ConnectionString"]);

builder.Services.AddSingleton<IMongoClient>(_ => mongoClient);
builder.Services.AddScoped<IMongoDatabase>(_ =>
    mongoClient.GetDatabase(mongoSettings["Database"]));

// ── Domain / Infrastructure ───────────────────────────────────────────────────
builder.Services.AddScoped<IFulfilmentRepository, MongoFulfilmentRepository>();

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<CreateFulfilmentHandler>();

// ── Elsa (with MongoDB persistence) ──────────────────────────────────────────
var elsaMongoConnection = builder.Configuration.GetConnectionString("ElsaMongo")!;

builder.Services.AddElsa(elsa =>
{
    elsa.UseWorkflowRuntime();
    elsa.UseMongoDb(elsaMongoConnection, options => options.DatabaseName = "elsa");
    elsa.AddWorkflow<FulfilmentWorkflow>();
});

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

app.MapPost("/api/fulfilments", async (
    CreateFulfilmentCommand cmd,
    CreateFulfilmentHandler handler,
    CancellationToken ct) =>
{
    await handler.HandleAsync(cmd, ct);
    return Results.Created($"/api/fulfilments/{cmd.FulfilmentId}", null);
});

app.Run();
