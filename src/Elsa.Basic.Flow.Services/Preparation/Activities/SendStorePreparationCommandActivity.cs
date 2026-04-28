using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Scheduling;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Elsa.Basic.Flow.Services.Preparation.Activities;

internal class SendStorePreparationCommandActivity : Activity
{
    private const int MaxAttempts = 4;

    // False on permanent failure (all retries exhausted) — caller checks and routes to fault path.
    public Output<bool>? Succeeded { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
        => await AttemptAsync(context);

    private async ValueTask AttemptAsync(ActivityExecutionContext context)
    {
        var props    = context.WorkflowExecutionContext.Properties;
        var countKey = $"_spca_{context.Activity.Id}";
        var count    = props.TryGetValue(countKey, out var r) ? Convert.ToInt32(r) : 0;

        props.TryGetValue(InitialiseStorePreparationPropertiesActivity.PrepCommandIdKey, out var pidRaw);
        props.TryGetValue(InitialiseStorePreparationPropertiesActivity.FulfilmentIdKey,  out var fidRaw);
        props.TryGetValue(InitialiseStorePreparationPropertiesActivity.StoreIdKey,       out var sidRaw);
        props.TryGetValue(InitialiseStorePreparationPropertiesActivity.LinesKey,         out var linesRaw);

        var prepCommandId = Guid.TryParse(pidRaw?.ToString(), out var pg) ? pg : Guid.Empty;
        var fulfilmentId  = Guid.TryParse(fidRaw?.ToString(), out var fg) ? fg : Guid.Empty;
        var storeId       = sidRaw?.ToString() ?? string.Empty;
        var lines         = JsonSerializer.Deserialize<List<OrderLine>>(linesRaw?.ToString() ?? "[]") ?? [];

        var config  = context.GetRequiredService<IConfiguration>();
        var factory = context.GetRequiredService<IHttpClientFactory>();
        var logger  = context.GetRequiredService<ILogger<SendStorePreparationCommandActivity>>();
        var baseUrl = config["Api:BaseUrl"] ?? "http://localhost:5000";

        logger.LogInformation("[StorePreparation] Dispatching prep={PrepId}  store={StoreId}  lines={Lines}  attempt={Attempt}/{Max}",
            prepCommandId, storeId, lines.Count, count + 1, MaxAttempts);

        bool ok;
        try
        {
            using var http = factory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);

            var response = await http.PostAsJsonAsync(
                $"{baseUrl}/api/preparations",
                new { PrepCommandId = prepCommandId, FulfilmentId = fulfilmentId, StoreId = storeId, Lines = lines },
                context.CancellationToken);

            ok = response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { throw; }
        catch { ok = false; }

        if (ok)
        {
            context.Set(Succeeded, true);
            await context.CompleteActivityAsync();
            return;
        }

        count++;

        if (count >= MaxAttempts)
        {
            logger.LogError("[StorePreparation] POST /api/preparations failed after {Max} attempts — prep={PrepId}", MaxAttempts, prepCommandId);
            context.Set(Succeeded, false);
            await context.CompleteActivityAsync();
            return;
        }

        props[countKey] = count;

        var delay    = TimeSpan.FromSeconds(Math.Pow(2, count));
        var resumeAt = DateTimeOffset.UtcNow.Add(delay);

        logger.LogWarning("[StorePreparation] Retry {Count}/{Max} scheduled in {Delay}s — prep={PrepId}", count, MaxAttempts, delay.TotalSeconds, prepCommandId);

        var bookmark  = context.CreateBookmark(new CreateBookmarkArgs { Callback = AttemptAsync, AutoBurn = true });
        var scheduler = context.GetRequiredService<IWorkflowScheduler>();
        await scheduler.ScheduleAtAsync(
            $"store-prep-send:{context.WorkflowExecutionContext.Id}:{context.Activity.Id}",
            new ScheduleExistingWorkflowInstanceRequest
            {
                WorkflowInstanceId = context.WorkflowExecutionContext.Id,
                BookmarkId         = bookmark.Id
            },
            resumeAt,
            context.CancellationToken);
    }
}
