using System.Text.Json;
using System.Net.Http.Json;
using Elsa.Basic.Flow.Services.Allocation;
using Elsa.Basic.Flow.Services.Preparation;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Basic.Flow.Services.Fulfilment;

internal class SendPrepareCommandsActivity : Activity
{
    public Input<AllocationResult>? AllocationResult { get; set; }
    public Output<List<PrepareCommand>>? PrepareCommands { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            var result = context.Get(AllocationResult)!;
            
            // Get FulfilmentId from workflow input
            var input = context.WorkflowExecutionContext.Input;
            var fulfilmentId = "unknown-fulfilment-id";
            if (input != null && input.TryGetValue("FulfilmentId", out var fulfilmentIdVal))
            {
                fulfilmentId = fulfilmentIdVal?.ToString() ?? "unknown-fulfilment-id";
            }
            
            var commands = result.StoreAllocations
                .Select(a => new PrepareCommand(Guid.NewGuid(), a.StoreId, a.Lines, fulfilmentId))
                .ToList();

            Console.WriteLine();
            Console.WriteLine($"[FulfilmentWorkflow] Creating {commands.Count} preparation request(s) for order {result.OrderNo}:");

            foreach (var cmd in commands)
            {
                Console.WriteLine($"  → PrepareCommand id={cmd.Id}  storeId={cmd.StoreId}  lines={cmd.Lines.Count}");
                foreach (var line in cmd.Lines)
                    Console.WriteLine($"      {line.Sku,-20} qty:{line.Quantity,3}  {line.UnitOfMeasure}");
            }

            // Store commands for next activity and trigger external preparation requests
            context.Set(PrepareCommands, commands);
            
            // Trigger external preparation via HTTP (non-blocking)
            var httpClientFactory = context.WorkflowExecutionContext.ServiceProvider.GetRequiredService<IHttpClientFactory>();
            var config = context.WorkflowExecutionContext.ServiceProvider.GetRequiredService<IConfiguration>();
            var baseUrl = config["Api:BaseUrl"] ?? "http://localhost:5000";
            
            using var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            
            foreach (var cmd in commands)
            {
                try
                {
                    // Fire-and-forget HTTP call to trigger external preparation
                    var response = await httpClient.PostAsJsonAsync($"{baseUrl}/api/preparation/prepare", cmd);
                    var status = response.IsSuccessStatusCode ? "✓" : "✗";
                    Console.WriteLine($"  {status} PrepareCommand id={cmd.Id} → External preparation triggered ({response.StatusCode})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ✗ PrepareCommand id={cmd.Id} → Failed to trigger preparation: {ex.Message}");
                }
            }

            await context.CompleteActivityAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FulfilmentWorkflow] Error in SendPrepareCommands: {ex.Message}");
            throw; // Re-throw to let workflow engine handle retries
        }
    }
}
