using Elsa.Basic.Flow.Infrastructure.Preparation;
using Elsa.Basic.Flow.Services.Fulfilment.Workflows;
using Elsa.Basic.Flow.Services.Preparation;
using Elsa.Basic.Flow.Services.Preparation.Workflows;
using Elsa.Extensions;
using Elsa.Persistence.MongoDb.Extensions;
using Elsa.Persistence.MongoDb.Modules.Management;
using Elsa.Persistence.MongoDb.Modules.Runtime;
using Elsa.Scheduling;
using Elsa.Common.RecurringTasks;
using Elsa.Workflows.CommitStates.Strategies;
using Elsa.Workflows.Features;
using Elsa.Workflows.Runtime.Options;
using Elsa.Workflows.Runtime.Tasks;
using Medallion.Threading.MongoDB;
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

        // Workflows stuck in Executing state longer than InactivityThreshold are considered crashed
        // and will be re-run by RestartInterruptedWorkflowsTask. Must be > activity HTTP timeout (30s).
        services.Configure<RuntimeOptions>(o => o.InactivityThreshold = TimeSpan.FromMinutes(2));
        // Scan for crashed workflows every 1 minute (default is every 5 minutes).
        services.Configure<RecurringTaskOptions>(o => o.Schedule.ConfigureTask<RestartInterruptedWorkflowsTask>(TimeSpan.FromMinutes(1)));

        services.AddElsa(elsa =>
        {
            elsa.UseMongoDb(elsaMongoConnection);
            elsa.UseWorkflowRuntime(r =>
            {
                r.UseMongoDb(_ => { });
                r.DistributedLockProvider = sp =>
                    new MongoDistributedSynchronizationProvider(
                        sp.GetRequiredService<IMongoDatabase>(),
                        "elsa_distributed_locks");
            });
            elsa.UseWorkflowManagement(m => m.UseMongoDb(_ => { }));
            elsa.UseScheduling(scheduling =>
            {
                scheduling.WorkflowScheduler = sp => sp.GetRequiredService<MongoWorkflowScheduler>();
            });
            // Commit workflow state to MongoDB before each activity executes so that if the
            // process crashes mid-activity, RestartInterruptedWorkflowsTask can re-run it
            // from the correct activity rather than from an earlier checkpoint.
            elsa.Configure<WorkflowsFeature>(w => w.WithDefaultActivityCommitStrategy(new ExecutingActivityStrategy()));
            elsa.AddWorkflow<FulfilmentWorkflow>();
            elsa.AddWorkflow<PreparationWorkflow>();
            elsa.AddWorkflow<StorePreparationSubWorkflow>();
        });

        return services;
    }
}
