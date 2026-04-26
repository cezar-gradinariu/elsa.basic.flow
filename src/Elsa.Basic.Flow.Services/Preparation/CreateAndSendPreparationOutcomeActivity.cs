using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Scheduling;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Elsa.Basic.Flow.Services.Preparation;

internal class CreateAndSendPreparationOutcomeActivity : Activity
{
    private const string RetryCountKey      = "_outcome_retry_count";
    private const string PrepareCommandKey  = "_outcome_prepare_command_id";
    private const string LinesJsonKey       = "_outcome_lines_json";
    private const string ContainersJsonKey  = "_outcome_containers_json";
    private const int    MaxAttempts        = 4;

    public Input<string>? PrepareCommandIdIn { get; set; }
    public Input<string>? LinesJsonIn        { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        // Seed Properties from inputs on first execute so the callback can read
        // them after a process restart (workflow variables survive via Elsa's
        // instance persistence, but reading through Input<T> in a callback
        // requires the expression context to be fully restored — Properties are
        // simpler and follow the same pattern as AllocateFulfilmentActivity).
        var props = context.WorkflowExecutionContext.Properties;

        var id        = Guid.Parse(context.Get(PrepareCommandIdIn) ?? throw new InvalidOperationException("PrepareCommandId is required"));
        var linesJson = context.Get(LinesJsonIn) ?? "[]";
        var lines     = JsonSerializer.Deserialize<List<OrderLine>>(linesJson) ?? [];

        var containers = BuildContainers(lines);

        props[PrepareCommandKey] = id.ToString();
        props[LinesJsonKey]      = linesJson;
        props[ContainersJsonKey] = JsonSerializer.Serialize(containers);

        var logger = context.GetRequiredService<ILogger<CreateAndSendPreparationOutcomeActivity>>();
        LogPayload(logger, id, containers);

        await TrySendOutcomeAsync(context);
    }

    private async ValueTask TrySendOutcomeAsync(ActivityExecutionContext context)
    {
        var props      = context.WorkflowExecutionContext.Properties;
        var retryCount = props.TryGetValue(RetryCountKey, out var r) ? Convert.ToInt32(r) : 0;

        var id         = Guid.Parse(props[PrepareCommandKey]?.ToString() ?? throw new InvalidOperationException("PrepareCommandId missing from properties"));
        var containers = JsonSerializer.Deserialize<List<PreparationContainer>>(props[ContainersJsonKey]?.ToString() ?? "[]") ?? [];
        var payload    = new PreparationOutcomePayload(id, containers);

        var config  = context.GetRequiredService<IConfiguration>();
        var factory = context.GetRequiredService<IHttpClientFactory>();
        var logger  = context.GetRequiredService<ILogger<CreateAndSendPreparationOutcomeActivity>>();
        var baseUrl = config["Api:BaseUrl"] ?? "http://localhost:5000";

        logger.LogInformation("[PreparationWorkflow] POST /api/preparation-outcome attempt {Attempt}/{Max}", retryCount + 1, MaxAttempts);

        using var http = factory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        bool succeeded;
        try
        {
            var response = await http.PostAsJsonAsync($"{baseUrl}/api/preparation-outcome", payload, context.CancellationToken);
            logger.LogInformation("[PreparationWorkflow] POST /api/preparation-outcome → {Status}", (int)response.StatusCode);
            succeeded = response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning("[PreparationWorkflow] POST failed (attempt {Attempt}/{Max}): {Message}", retryCount + 1, MaxAttempts, ex.Message);
            succeeded = false;
        }

        if (succeeded)
        {
            await context.CompleteActivityAsync();
            return;
        }

        retryCount++;

        if (retryCount >= MaxAttempts)
        {
            logger.LogError("[PreparationWorkflow] POST /api/preparation-outcome failed after {Max} attempts — aborting", MaxAttempts);
            throw new HttpRequestException($"POST /api/preparation-outcome failed after {MaxAttempts} attempts");
        }

        props[RetryCountKey] = retryCount;

        var delay    = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
        var resumeAt = DateTimeOffset.UtcNow.Add(delay);

        logger.LogWarning("[PreparationWorkflow] Retry {Attempt}/{Max} persisted — resuming in {Delay}s",
            retryCount, MaxAttempts, delay.TotalSeconds);

        var bookmark  = context.CreateBookmark(new CreateBookmarkArgs { Callback = TrySendOutcomeAsync, AutoBurn = true });
        var scheduler = context.GetRequiredService<IWorkflowScheduler>();
        await scheduler.ScheduleAtAsync(
            $"outcome-retry:{context.WorkflowExecutionContext.Id}",
            new ScheduleExistingWorkflowInstanceRequest
            {
                WorkflowInstanceId = context.WorkflowExecutionContext.Id,
                BookmarkId         = bookmark.Id
            },
            resumeAt,
            context.CancellationToken);
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
            logger.LogInformation("  {ContainerId} ({ContainerType})  lines={Lines}", c.ContainerId, c.ContainerType, c.AllocatedLines.Count);
    }
}
