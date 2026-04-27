using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkflowCore.Basic.Flow.Domain;
using WorkflowCore.Interface;
using WorkflowCore.Models;

namespace WorkflowCore.Basic.Flow.Services.Preparation.Steps;

public class LogPreparationStep(ILogger<LogPreparationStep> logger) : IStepBody
{
    // Inputs
    public Guid   PrepCommandId { get; set; }
    public string StoreId       { get; set; } = "";
    public string LinesJson     { get; set; } = "[]";

    // Outputs
    public int    DelaySeconds   { get; set; }
    public string ContainersJson { get; set; } = "[]";

    public Task<ExecutionResult> RunAsync(IStepExecutionContext context)
    {
        var lines         = JsonSerializer.Deserialize<List<OrderLine>>(LinesJson) ?? [];
        var randomSeconds = Random.Shared.Next(5, 31);
        DelaySeconds      = randomSeconds;

        if (lines.Count == 0)
            logger.LogWarning("[PreparationWorkflow] No order lines for {Id} — will produce 0 containers", PrepCommandId);

        var containers   = BuildContainers(lines);
        ContainersJson   = JsonSerializer.Serialize(containers);

        logger.LogInformation("[PreparationWorkflow] id={Id}  store={StoreId}  lines={LineCount}  delay={Delay}s",
            PrepCommandId, StoreId, lines.Count, randomSeconds);

        foreach (var c in containers)
            logger.LogInformation("  {ContainerId} ({ContainerType})  lines={Lines}", c.ContainerId, c.ContainerType, c.AllocatedLines.Count);

        return Task.FromResult(ExecutionResult.Next());
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
}
