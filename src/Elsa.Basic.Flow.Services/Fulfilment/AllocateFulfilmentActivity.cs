using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Basic.Flow.Services.Allocation;
using Elsa.Scheduling;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Elsa.Basic.Flow.Services.Fulfilment;

internal class AllocateFulfilmentActivity : Activity
{
    private const string RetryCountKey    = "_alloc_retry_count";
    private const string FulfilmentIdKey  = "_alloc_fulfilment_id";
    private const string OrderNoKey       = "_alloc_order_no";
    private const string StoreIdKey       = "_alloc_store_id";
    private const string OrderLinesKey    = "_alloc_order_lines";
    private const int    MaxAttempts      = 4;

    public Output<AllocationResult>? Result       { get; set; }
    public Output<string>?           FulfilmentId { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
        => await TryAllocateAsync(context);

    private async ValueTask TryAllocateAsync(ActivityExecutionContext context)
    {
        var props      = context.WorkflowExecutionContext.Properties;
        var retryCount = props.TryGetValue(RetryCountKey, out var r) ? Convert.ToInt32(r) : 0;

        // On first run, copy Input values into workflow Properties so they survive
        // process restarts. WorkflowExecutionContext.Input is not restored on restart
        // by DefaultWorkflowRestarter — only Properties (part of WorkflowState) are.
        if (!props.ContainsKey(FulfilmentIdKey))
        {
            var input = context.WorkflowExecutionContext.Input;
            object? fid = null, ono = null, sid = null, ol = null;
            input?.TryGetValue("FulfilmentId", out fid);
            input?.TryGetValue("OrderNo",      out ono);
            input?.TryGetValue("StoreId",      out sid);
            input?.TryGetValue("OrderLines",   out ol);
            props[FulfilmentIdKey] = fid?.ToString() ?? "unknown";
            props[OrderNoKey]      = ono?.ToString() ?? string.Empty;
            props[StoreIdKey]      = sid?.ToString() ?? string.Empty;
            props[OrderLinesKey]   = ol?.ToString()  ?? "[]";
        }

        var fulfilmentId = props[FulfilmentIdKey]?.ToString() ?? "unknown";
        var orderNo      = props[OrderNoKey]?.ToString()      ?? string.Empty;
        var storeId      = props[StoreIdKey]?.ToString()      ?? string.Empty;
        var orderLines   = JsonSerializer.Deserialize<List<OrderLine>>(props[OrderLinesKey]?.ToString() ?? "[]") ?? [];
        var cmd          = new CreateAllocationCommand(orderNo, storeId, orderLines);

        var config  = context.GetRequiredService<IConfiguration>();
        var factory = context.GetRequiredService<IHttpClientFactory>();
        var logger  = context.GetRequiredService<ILogger<AllocateFulfilmentActivity>>();
        var baseUrl = config["Api:BaseUrl"] ?? "http://localhost:5000";

        using var http = factory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        logger.LogInformation("[FulfilmentWorkflow] Allocation attempt {Attempt}/{Max}", retryCount + 1, MaxAttempts);

        var response = await http.PostAsJsonAsync($"{baseUrl}/api/allocations", cmd, context.CancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<AllocationResult>(context.CancellationToken);
            context.Set(FulfilmentId, fulfilmentId);
            context.Set(Result, result);
            await context.CompleteActivityAsync();
            return;
        }

        retryCount++;

        if (retryCount >= MaxAttempts)
        {
            logger.LogError("[FulfilmentWorkflow] Allocation failed after {Max} attempts — aborting", MaxAttempts);
            throw new HttpRequestException($"POST /api/allocations failed after {MaxAttempts} attempts: {response.StatusCode}");
        }

        props[RetryCountKey] = retryCount;

        var delay    = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
        var resumeAt = DateTimeOffset.UtcNow.Add(delay);

        logger.LogWarning("[FulfilmentWorkflow] Allocation attempt {Attempt} failed ({Status}) — retry persisted, resuming in {Delay}s",
            retryCount, (int)response.StatusCode, delay.TotalSeconds);

        // Create a durable bookmark so the retry survives a process restart.
        // The scheduler writes to elsa_scheduled_tasks (MongoDB), which the
        // SchedulerPollingService drains on startup — same mechanism as Delay.
        var bookmark  = context.CreateBookmark(new CreateBookmarkArgs { Callback = TryAllocateAsync, AutoBurn = true });
        var scheduler = context.GetRequiredService<IWorkflowScheduler>();
        await scheduler.ScheduleAtAsync(
            $"alloc-retry:{context.WorkflowExecutionContext.Id}",
            new ScheduleExistingWorkflowInstanceRequest
            {
                WorkflowInstanceId = context.WorkflowExecutionContext.Id,
                BookmarkId         = bookmark.Id
            },
            resumeAt,
            context.CancellationToken);
    }
}
