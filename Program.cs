using Elsa.Basic.Flow.Scenarios;
using Elsa.Basic.Flow.Scenarios.Approval;
using Elsa.Basic.Flow.Scenarios.BothSignals;
using Elsa.Basic.Flow.Scenarios.CountdownTimer;
using Elsa.Basic.Flow.Scenarios.Greeting;
using Elsa.Basic.Flow.Scenarios.OrderAllocation;
using Elsa.Basic.Flow.Scenarios.ParallelSubflows;
using Elsa.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ── Register all scenarios here ──────────────────────────────────────────────
IWorkflowScenario[] scenarios =
[
    new GreetingScenario(),
    new ApprovalScenario(),
    new BothSignalsScenario(),
    new ParallelDetailsScenario(),
    new OrderAllocationScenario(),
    new CountdownTimerScenario(),
];
// ─────────────────────────────────────────────────────────────────────────────

Console.WriteLine("=== Elsa Basic Flow ===");
Console.WriteLine();
for (var i = 0; i < scenarios.Length; i++)
    Console.WriteLine($"  [{i + 1}] {scenarios[i].Name} — {scenarios[i].Description}");
Console.WriteLine();
Console.Write("Pick a scenario: ");

var pick = int.TryParse(Console.ReadLine(), out var n) ? n - 1 : 0;
var selected = scenarios[Math.Clamp(pick, 0, scenarios.Length - 1)];

Console.WriteLine();

var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.SetMinimumLevel(LogLevel.Warning);
        logging.AddFilter("Elsa", LogLevel.Warning);
    })
    .ConfigureServices(services =>
    {
        services.AddElsa(selected.Register);
    })
    .Build();

await host.StartAsync();
await selected.RunAsync(host.Services);
await host.StopAsync();
