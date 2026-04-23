using Elsa.Basic.Flow.Infrastructure.Preparation;
using Elsa.Basic.Flow.Services.Fulfilment;
using Elsa.Basic.Flow.Services.Preparation;
using Elsa.Extensions;
using Elsa.Persistence.MongoDb.Extensions;
using Elsa.Persistence.MongoDb.Modules.Management;
using Elsa.Persistence.MongoDb.Modules.Runtime;
using Elsa.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Elsa.Basic.Flow.Infrastructure;

public static class WorkflowInfrastructureExtensions
{
    public static IServiceCollection AddWorkflowInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var elsaMongoConnection = configuration.GetConnectionString("ElsaMongo")!;

        services.AddSingleton<SchedulerWakeSignal>();
        services.AddSingleton<MongoWorkflowScheduler>();
        services.AddHostedService<SchedulerPollingService>();
        services.AddHostedService<MongoIndexInitialiser>();

        services.AddScoped<IPreparationOutcomeHandler, ElsaPreparationOutcomeHandler>();

        services.AddElsa(elsa =>
        {
            elsa.UseMongoDb(elsaMongoConnection);
            elsa.UseWorkflowRuntime(r => r.UseMongoDb(_ => { }));
            elsa.UseWorkflowManagement(m => m.UseMongoDb(_ => { }));
            elsa.UseScheduling(scheduling =>
            {
                scheduling.WorkflowScheduler = sp => sp.GetRequiredService<MongoWorkflowScheduler>();
            });
            elsa.AddWorkflow<FulfilmentWorkflow>();
            elsa.AddWorkflow<PreparationWorkflow>();
        });

        return services;
    }
}
