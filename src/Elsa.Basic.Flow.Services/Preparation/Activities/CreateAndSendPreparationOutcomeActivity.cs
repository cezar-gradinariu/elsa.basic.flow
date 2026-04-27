using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Basic.Flow.Services.Common;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Elsa.Basic.Flow.Services.Preparation;

internal class CreateAndSendPreparationOutcomeActivity : DurableRetryActivity
{
    private const string PrepareCommandKey = "_outcome_prepare_command_id";
    private const string ContainersJsonKey = "_outcome_containers_json";

    public Input<string>? PrepareCommandIdIn { get; set; }
    public Input<string>? LinesJsonIn        { get; set; }

    protected override async ValueTask<bool> TryExecuteAsync(ActivityExecutionContext context)
    {
        var props  = context.WorkflowExecutionContext.Properties;
        var logger = context.GetRequiredService<ILogger<CreateAndSendPreparationOutcomeActivity>>();

        if (!props.ContainsKey(PrepareCommandKey))
        {
            var id        = Guid.Parse(context.Get(PrepareCommandIdIn) ?? throw new InvalidOperationException("PrepareCommandId is required"));
            var linesJson = context.Get(LinesJsonIn) ?? "[]";
            var containers = BuildContainers(JsonSerializer.Deserialize<List<OrderLine>>(linesJson) ?? []);

            props[PrepareCommandKey] = id.ToString();
            props[ContainersJsonKey] = JsonSerializer.Serialize(containers);

            LogPayload(logger, id, containers);
        }

        var payloadId  = Guid.Parse(props[PrepareCommandKey]!.ToString()!);
        var payload    = new PreparationOutcomePayload(
            payloadId,
            JsonSerializer.Deserialize<List<PreparationContainer>>(props[ContainersJsonKey]!.ToString()!) ?? []);

        var config  = context.GetRequiredService<IConfiguration>();
        var factory = context.GetRequiredService<IHttpClientFactory>();
        var baseUrl = config["Api:BaseUrl"] ?? "http://localhost:5000";

        using var http = factory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        try
        {
            var response = await http.PostAsJsonAsync($"{baseUrl}/api/preparation-outcome", payload, context.CancellationToken);
            logger.LogInformation("[PreparationWorkflow] POST /api/preparation-outcome → {Status}", (int)response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning("[PreparationWorkflow] POST /api/preparation-outcome threw: {Message}", ex.Message);
            return false;
        }
    }

    private static List<PreparationContainer> BuildContainers(List<OrderLine> lines)
    {
        if (lines.Count == 0) return [];

        var containerCount = Random.Shared.Next(1, Math.Min(4, lines.Count + 1));
        var buckets = Enumerable.Range(0, containerCount)
            .Select(_ => new List<AllocatedLine>())
            .ToList();

        for (var i = 0; i < lines.Count; i++)
            buckets[i % containerCount].Add(new AllocatedLine(lines[i].Sku, lines[i].Sku, lines[i].Quantity));

        var containerTypes = Enum.GetValues<ContainerType>();
        return buckets
            .Select((bucket, i) => new PreparationContainer(
                ContainerId:    $"CONT-{i + 1:D2}",
                ContainerType:  containerTypes[Random.Shared.Next(containerTypes.Length)],
                AllocatedLines: bucket))
            .ToList();
    }

    private static void LogPayload(ILogger logger, Guid id, List<PreparationContainer> containers)
    {
        logger.LogInformation("[PreparationWorkflow] Building outcome for {Id} ({Count} container(s))", id, containers.Count);
        foreach (var c in containers)
            logger.LogInformation("  {ContainerId} ({ContainerType})  lines={Lines}", c.ContainerId, c.ContainerType, c.AllocatedLines.Count);
    }
}
