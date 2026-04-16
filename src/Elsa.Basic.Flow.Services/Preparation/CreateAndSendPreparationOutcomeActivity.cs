using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Microsoft.Extensions.Configuration;

namespace Elsa.Basic.Flow.Services.Preparation;

internal class CreateAndSendPreparationOutcomeActivity : Activity
{
    public Input<string>? PrepareCommandIdIn { get; set; }
    public Input<string>? LinesJsonIn        { get; set; }
    public Input<string>? FulfilmentIdIn     { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var id           = Guid.TryParse(context.Get(PrepareCommandIdIn), out var parsed) ? parsed : Guid.NewGuid();
        var linesJson    = context.Get(LinesJsonIn)    ?? "[]";
        var fulfilmentId = Guid.TryParse(context.Get(FulfilmentIdIn), out var pf) ? pf : Guid.Empty;
        var lines        = JsonSerializer.Deserialize<List<OrderLine>>(linesJson) ?? [];

        if (lines.Count == 0)
            Console.WriteLine($"[PreparationWorkflow] ⚠️  No lines for preparation {id}");

        var containers = BuildContainers(lines);
        var payload    = new PreparationOutcomePayload(id, fulfilmentId, containers);

        LogPayload(id, containers);

        var config   = context.GetRequiredService<IConfiguration>();
        var factory  = context.GetRequiredService<IHttpClientFactory>();
        var baseUrl  = config["Api:BaseUrl"] ?? "http://localhost:5000";

        using var http = factory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        var response = await http.PostAsJsonAsync($"{baseUrl}/api/preparation-outcome", payload);
        Console.WriteLine($"[PreparationWorkflow] POST /api/preparation-outcome → {(int)response.StatusCode} {response.ReasonPhrase}");

        await context.CompleteActivityAsync();
    }

    private static List<PreparationContainer> BuildContainers(List<OrderLine> lines)
    {
        if (lines.Count == 0) return [];

        var containerCount = Random.Shared.Next(1, Math.Min(4, lines.Count + 1));
        var buckets = Enumerable.Range(0, containerCount)
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
        Console.WriteLine($"[PreparationWorkflow] Outcome for {id} ({containers.Count} container(s)):");
        foreach (var c in containers)
        {
            Console.WriteLine($"  📦 {c.ContainerId} ({c.ContainerType})  lines={c.AllocatedLines.Count}");
            foreach (var l in c.AllocatedLines)
                Console.WriteLine($"    {l.OrderLineNo,-15} qty:{l.Quantity,3}");
        }
    }
}
