using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkflowCore.Basic.Flow.Domain;
using WorkflowCore.Basic.Flow.Services.Allocation;
using WorkflowCore.Interface;
using WorkflowCore.Models;

namespace WorkflowCore.Basic.Flow.Services.Fulfilment.Steps;

public class AllocateFulfilmentStep(
    IHttpClientFactory              factory,
    IConfiguration                  config,
    ILogger<AllocateFulfilmentStep> logger) : IStepBody
{
    // Inputs
    public string OrderNo   { get; set; } = "";
    public string StoreId   { get; set; } = "";
    public string LinesJson { get; set; } = "[]";

    // Output
    public string AllocationJson { get; set; } = "";

    public async Task<ExecutionResult> RunAsync(IStepExecutionContext context)
    {
        var baseUrl = config["Api:BaseUrl"] ?? throw new InvalidOperationException("Api:BaseUrl is not configured.");
        var lines   = JsonSerializer.Deserialize<List<OrderLine>>(LinesJson) ?? [];

        logger.LogInformation("[FulfilmentWorkflow] Requesting allocation for order {OrderNo}", OrderNo);

        using var http = factory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        var response = await http.PostAsJsonAsync(
            $"{baseUrl}/api/allocations",
            new CreateAllocationCommand(OrderNo, StoreId, lines),
            context.CancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("[FulfilmentWorkflow] Allocation returned {Status} — will retry", (int)response.StatusCode);
            throw new HttpRequestException($"Allocation returned {(int)response.StatusCode}");
        }

        var result    = await response.Content.ReadFromJsonAsync<AllocationResult>(context.CancellationToken);
        AllocationJson = JsonSerializer.Serialize(result);
        logger.LogInformation("[FulfilmentWorkflow] Allocation succeeded");
        return ExecutionResult.Next();
    }
}
