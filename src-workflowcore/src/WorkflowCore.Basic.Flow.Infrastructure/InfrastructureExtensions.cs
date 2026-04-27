using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using WorkflowCore.Basic.Flow.Domain;
using WorkflowCore.Basic.Flow.Services.Allocation;
using WorkflowCore.Basic.Flow.Services.Fulfilment;
using WorkflowCore.Basic.Flow.Services.Fulfilment.Commands;
using WorkflowCore.Basic.Flow.Services.Fulfilment.Steps;
using WorkflowCore.Basic.Flow.Services.Preparation;
using WorkflowCore.Basic.Flow.Services.Preparation.Commands;
using WorkflowCore.Basic.Flow.Services.Preparation.Steps;

namespace WorkflowCore.Basic.Flow.Infrastructure;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddWorkflowInfrastructure(
        this IServiceCollection services,
        IMongoClient            mongoClient,
        IConfiguration          config)
    {
        var domainDb      = config["MongoDB:Database"]  ?? "fms-wc";
        var workflowDb    = config["MongoDB:WorkflowDatabase"] ?? "fms-wc-workflows";
        var mongoConnStr  = config["MongoDB:ConnectionString"]!;

        // Domain MongoDB database
        services.AddSingleton<IMongoDatabase>(_ => mongoClient.GetDatabase(domainDb));

        // WorkflowCore engine with MongoDB persistence
        services.AddWorkflow(cfg => cfg.UseMongoDB(
            mongoConnStr,
            workflowDb,
            configureClient:      null,
            serializerTypeFilter: t => ObjectSerializer.DefaultAllowedTypes(t) ||
                                       t.FullName?.StartsWith("WorkflowCore.") == true));

        // Domain
        services.AddSingleton<IFulfilmentRepository, MongoFulfilmentRepository>();

        // Application handlers
        services.AddScoped<CreateFulfilmentHandler>();
        services.AddScoped<CreatePreparationHandler>();
        services.AddScoped<ApplyContainersHandler>();
        services.AddSingleton<CreateAllocationHandler>();

        // WorkflowCore step bodies (each step execution gets its own instance)
        services.AddTransient<AllocateFulfilmentStep>();
        services.AddTransient<SendPrepareCommandsStep>();
        services.AddTransient<IncrementPreparationCountStep>();
        services.AddTransient<CompleteFulfilmentStep>();
        services.AddTransient<LogPreparationStep>();
        services.AddTransient<SendPreparationOutcomeStep>();

        return services;
    }
}
