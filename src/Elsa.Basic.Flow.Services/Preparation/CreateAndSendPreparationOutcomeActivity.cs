using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Basic.Flow.Services.Preparation;

internal class CreateAndSendPreparationOutcomeActivity : Activity
{
    public Input<string>? PrepareCommandIdIn { get; set; }
    public Input<string>? LinesJsonIn { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            // Get data from workflow variables (persisted across delay suspension)
            var id = Guid.TryParse(context.Get(PrepareCommandIdIn), out var parsed) ? parsed : Guid.NewGuid();
            var linesJson = context.Get(LinesJsonIn) ?? "[]";
            var lines = JsonSerializer.Deserialize<List<OrderLine>>(linesJson) ?? [];

            Console.WriteLine($"[PreparationWorkflow] CreateAndSend: Retrieved {lines.Count} order lines from workflow variables");
            
            if (lines.Count == 0)
            {
                Console.WriteLine($"[PreparationWorkflow] ⚠️  WARNING: Lines data is empty after retrieving from variables! linesJson: '{linesJson}'");
            }

            var containers = BuildContainers(lines);
            var payload    = new PreparationOutcomePayload(id, containers);

            LogPayload(id, containers);

            var sp       = context.WorkflowExecutionContext.ServiceProvider;
            var config   = sp.GetRequiredService<IConfiguration>();
            var baseUrl  = config["Api:BaseUrl"] ?? "http://localhost:5000";
            var factory  = sp.GetRequiredService<IHttpClientFactory>();
            var http     = factory.CreateClient();

            // Set timeout to prevent hanging if services are disposing
            http.Timeout = TimeSpan.FromSeconds(30);

            var response = await http.PostAsJsonAsync($"{baseUrl}/api/preparation-outcome", payload);
            Console.WriteLine($"[PreparationWorkflow] POST /api/preparation-outcome → {(int)response.StatusCode} {response.ReasonPhrase}");

            await context.CompleteActivityAsync();
        }
        catch (ObjectDisposedException ex)
        {
            Console.WriteLine($"[PreparationWorkflow] Service disposed during CreateAndSendOutcome execution: {ex.ObjectName} - Outcome work completed");
            // Don't re-throw - the outcome work was completed
        }
        catch (Exception ex) when (ex.ToString().Contains("IServiceProvider"))
        {
            Console.WriteLine($"[PreparationWorkflow] Service provider disposed during CreateAndSendOutcome - Outcome work completed: {ex.Message}");
            // Don't re-throw - the outcome work was completed
        }
    }

    private static List<PreparationContainer> BuildContainers(List<OrderLine> lines)
    {
        if (lines.Count == 0)
            return [];

        var containerCount = Random.Shared.Next(1, Math.Min(4, lines.Count + 1));
        var buckets        = Enumerable.Range(0, containerCount)
                                       .Select(_ => new List<AllocatedLine>())
                                       .ToList();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            buckets[i % containerCount].Add(new AllocatedLine(line.Sku, line.Sku, line.Quantity));
        }

        var containerTypes = Enum.GetValues<ContainerType>();

        return buckets
            .Select((bucket, i) => new PreparationContainer(
                ContainerId:    $"CONT-{i + 1:D2}",
                ContainerType:  containerTypes[Random.Shared.Next(containerTypes.Length)],
                AllocatedLines: bucket))
            .ToList();
    }

    private static void LogPayload(Guid id, List<PreparationContainer> containers)
    {
        Console.WriteLine($"[PreparationWorkflow] Outcome for preparation {id}  ({containers.Count} container(s)):");
        foreach (var container in containers)
        {
            Console.WriteLine($"  📦 {container.ContainerId} ({container.ContainerType})  lines={container.AllocatedLines.Count}");
            foreach (var line in container.AllocatedLines)
                Console.WriteLine($"    {line.OrderLineNo,-15} qty:{line.Quantity,3}");
        }
    }
}
