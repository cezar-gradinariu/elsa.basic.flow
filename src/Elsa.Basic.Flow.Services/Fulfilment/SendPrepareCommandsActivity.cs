using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Basic.Flow.Services.Allocation;
using Elsa.Basic.Flow.Services.Preparation;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Basic.Flow.Services.Fulfilment;

internal class SendPrepareCommandsActivity : Activity
{
    public Input<AllocationResult>? AllocationResult { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var result   = context.Get(AllocationResult)!;
        var commands = result.StoreAllocations
            .Select(a => new PrepareCommand(Guid.NewGuid(), a.StoreId, a.Lines))
            .ToList();

        var sp      = context.WorkflowExecutionContext.ServiceProvider;
        var config  = sp.GetRequiredService<IConfiguration>();
        var baseUrl = config["Api:BaseUrl"] ?? "http://localhost:5000";
        var http    = sp.GetRequiredService<IHttpClientFactory>().CreateClient();

        Console.WriteLine();
        Console.WriteLine($"[FulfilmentWorkflow] Sending {commands.Count} PrepareCommand(s) in parallel for order {result.OrderNo}:");

        foreach (var cmd in commands)
        {
            Console.WriteLine($"  → PrepareCommand id={cmd.Id}  storeId={cmd.StoreId}  lines={cmd.Lines.Count}");
            foreach (var line in cmd.Lines)
                Console.WriteLine($"      {line.Sku,-20} qty:{line.Quantity,3}  {line.UnitOfMeasure}");
        }

        await Task.WhenAll(commands.Select(async cmd =>
        {
            var response = await http.PostAsJsonAsync($"{baseUrl}/api/preparation/prepare", cmd);
            Console.WriteLine($"  ✓ PrepareCommand id={cmd.Id} → {(int)response.StatusCode} {response.ReasonPhrase}");
        }));

        await context.CompleteActivityAsync();
    }
}
