using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Basic.Flow.Services.Allocation;
using Elsa.Basic.Flow.Services.Common;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Microsoft.Extensions.Configuration;

namespace Elsa.Basic.Flow.Services.Fulfilment;

internal class AllocateFulfilmentActivity : DurableRetryActivity
{
    public Output<AllocationResult>? Result      { get; set; }
    public Output<string>?           FulfilmentId { get; set; }

    protected override async ValueTask<bool> TryExecuteAsync(ActivityExecutionContext context)
    {
        var props = context.WorkflowExecutionContext.Properties;

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
        var baseUrl = config["Api:BaseUrl"] ?? "http://localhost:5000";

        using var http = factory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        try
        {
            var response = await http.PostAsJsonAsync(
                $"{baseUrl}/api/allocations",
                new CreateAllocationCommand(orderNo, storeId, orderLines),
                context.CancellationToken);

            if (!response.IsSuccessStatusCode) return false;

            var result = await response.Content.ReadFromJsonAsync<AllocationResult>(context.CancellationToken);
            context.Set(FulfilmentId, fulfilmentId);
            context.Set(Result, result);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
