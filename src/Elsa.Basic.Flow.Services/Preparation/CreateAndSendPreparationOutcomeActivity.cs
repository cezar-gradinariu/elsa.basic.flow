using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Basic.Flow.Services.Preparation;

internal class CreateAndSendPreparationOutcomeActivity : Activity
{
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var input = context.WorkflowExecutionContext.Input;

        object? idVal = null, linesVal = null;
        input?.TryGetValue("PrepareCommandId", out idVal);
        input?.TryGetValue("Lines",            out linesVal);

        var id    = Guid.TryParse(idVal?.ToString(), out var parsed) ? parsed : Guid.NewGuid();
        var lines = JsonSerializer.Deserialize<List<OrderLine>>(linesVal?.ToString() ?? "[]") ?? [];

        var containers = BuildContainers(lines);
        var payload    = new PreparationOutcomePayload(id, containers);

        LogPayload(id, containers);

        var sp       = context.WorkflowExecutionContext.ServiceProvider;
        var config   = sp.GetRequiredService<IConfiguration>();
        var baseUrl  = config["Api:BaseUrl"] ?? "http://localhost:5000";
        var factory  = sp.GetRequiredService<IHttpClientFactory>();
        var http     = factory.CreateClient();

        var response = await http.PostAsJsonAsync($"{baseUrl}/api/preparation-outcome", payload);
        Console.WriteLine($"[PreparationWorkflow] POST /api/preparation-outcome → {(int)response.StatusCode} {response.ReasonPhrase}");

        await context.CompleteActivityAsync();
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
        foreach (var c in containers)
        {
            Console.WriteLine($"  [{c.ContainerId}] {c.ContainerType}  lines={c.AllocatedLines.Count}");
            foreach (var l in c.AllocatedLines)
                Console.WriteLine($"      {l.OrderLineNo,-20} articleId:{l.ArticleId,-20} qty:{l.Quantity,3}");
        }
    }
}
