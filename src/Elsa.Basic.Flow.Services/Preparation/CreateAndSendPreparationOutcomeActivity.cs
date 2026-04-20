using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Microsoft.Extensions.Logging;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Microsoft.Extensions.Configuration;

namespace Elsa.Basic.Flow.Services.Preparation;

internal class CreateAndSendPreparationOutcomeActivity : Activity
{
    public Input<string>? PrepareCommandIdIn { get; set; }
    public Input<string>? LinesJsonIn        { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var id        = Guid.Parse(context.Get(PrepareCommandIdIn) ?? throw new InvalidOperationException("PrepareCommandId is required"));
        var linesJson = context.Get(LinesJsonIn) ?? "[]";
        var lines     = JsonSerializer.Deserialize<List<OrderLine>>(linesJson) ?? [];

        var logger = context.GetRequiredService<ILogger<CreateAndSendPreparationOutcomeActivity>>();

        if (lines.Count == 0)
            logger.LogWarning("[PreparationWorkflow] No lines for preparation {Id}", id);

        var containers = BuildContainers(lines);
        var payload    = new PreparationOutcomePayload(id, containers);

        LogPayload(logger, id, containers);

        var config  = context.GetRequiredService<IConfiguration>();
        var factory = context.GetRequiredService<IHttpClientFactory>();
        var baseUrl = config["Api:BaseUrl"] ?? "http://localhost:5000";

        using var http = factory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        const int maxAttempts = 4;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await http.PostAsJsonAsync($"{baseUrl}/api/preparation-outcome", payload, context.CancellationToken);
                logger.LogInformation("[PreparationWorkflow] POST /api/preparation-outcome → {Status}", (int)response.StatusCode);
                if (response.IsSuccessStatusCode) break;

                if (attempt == maxAttempts)
                    throw new HttpRequestException($"POST /api/preparation-outcome failed after {maxAttempts} attempts: {response.StatusCode}");
            }
            catch (HttpRequestException) when (attempt == maxAttempts)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning("[PreparationWorkflow] POST failed (attempt {Attempt}/{Max}): {Message}", attempt, maxAttempts, ex.Message);
            }

            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
            logger.LogInformation("[PreparationWorkflow] Retrying in {Delay}s", delay.TotalSeconds);
            await Task.Delay(delay, context.CancellationToken);
        }

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

    private static void LogPayload(ILogger<CreateAndSendPreparationOutcomeActivity> logger, Guid id, List<PreparationContainer> containers)
    {
        logger.LogInformation("[PreparationWorkflow] Outcome for {Id} ({Count} container(s))", id, containers.Count);
        foreach (var c in containers)
            logger.LogInformation("  📦 {ContainerId} ({ContainerType})  lines={Lines}", c.ContainerId, c.ContainerType, c.AllocatedLines.Count);
    }
}
