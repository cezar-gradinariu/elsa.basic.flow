using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Basic.Flow.Services.Allocation;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Elsa.Basic.Flow.Services.Fulfilment;

internal class AllocateFulfilmentActivity : Activity
{
    internal const string RetryCountKey = "_alloc_retry_count";
    private  const int    MaxAttempts   = 4;

    public Output<AllocationResult>? Result      { get; set; }
    public Output<string>?           FulfilmentId { get; set; }
    public Output<bool>?             Succeeded   { get; set; }
    public Output<TimeSpan>?         NextDelay   { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var props      = context.WorkflowExecutionContext.Properties;
        var retryCount = props.TryGetValue(RetryCountKey, out var r) ? Convert.ToInt32(r) : 0;

        props.TryGetValue(InitialiseFulfilmentPropertiesActivity.FulfilmentIdKey, out var fidRaw);
        props.TryGetValue(InitialiseFulfilmentPropertiesActivity.OrderNoKey,      out var onoRaw);
        props.TryGetValue(InitialiseFulfilmentPropertiesActivity.StoreIdKey,      out var sidRaw);
        props.TryGetValue(InitialiseFulfilmentPropertiesActivity.OrderLinesKey,   out var olRaw);

        var fulfilmentId = fidRaw?.ToString() ?? "unknown";
        var orderNo      = onoRaw?.ToString() ?? string.Empty;
        var storeId      = sidRaw?.ToString() ?? string.Empty;
        var orderLines   = JsonSerializer.Deserialize<List<OrderLine>>(olRaw?.ToString() ?? "[]") ?? [];

        var config  = context.GetRequiredService<IConfiguration>();
        var factory = context.GetRequiredService<IHttpClientFactory>();
        var logger  = context.GetRequiredService<ILogger<AllocateFulfilmentActivity>>();
        var baseUrl = config["Api:BaseUrl"] ?? "http://localhost:5000";

        logger.LogInformation("[FulfilmentWorkflow] Allocation attempt {Attempt}/{Max}", retryCount + 1, MaxAttempts);

        using var http = factory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        var response = await http.PostAsJsonAsync($"{baseUrl}/api/allocations", new CreateAllocationCommand(orderNo, storeId, orderLines), context.CancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<AllocationResult>(context.CancellationToken);
            context.Set(FulfilmentId, fulfilmentId);
            context.Set(Result, result);
            context.Set(Succeeded, true);
            await context.CompleteActivityAsync();
            return;
        }

        retryCount++;
        props[RetryCountKey] = retryCount;

        if (retryCount >= MaxAttempts)
        {
            logger.LogError("[FulfilmentWorkflow] Allocation failed after {Max} attempts — aborting", MaxAttempts);
            throw new HttpRequestException($"POST /api/allocations failed after {MaxAttempts} attempts: {response.StatusCode}");
        }

        var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
        logger.LogWarning("[FulfilmentWorkflow] Attempt {Attempt} failed ({Status}) — retrying in {Delay}s",
            retryCount, (int)response.StatusCode, delay.TotalSeconds);

        context.Set(Succeeded, false);
        context.Set(NextDelay, delay);
        await context.CompleteActivityAsync();
    }
}
