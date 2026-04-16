using Elsa.Features.Services;

namespace Elsa.Basic.Flow.Scenarios;

public interface IWorkflowScenario
{
    string Name { get; }
    string Description { get; }
    void Register(IModule elsa);
    Task RunAsync(IServiceProvider services, CancellationToken ct = default);
}
