using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkflowCore.Basic.Flow.Domain;
using WorkflowCore.Interface;
using WorkflowCore.Models;

namespace WorkflowCore.Basic.Flow.Services.Preparation.Steps;

public class SendPreparationOutcomeStep(
    IHttpClientFactory                  factory,
    IConfiguration                      config,
    ILogger<SendPreparationOutcomeStep> logger) : IStepBody
{
    // Inputs
    public Guid   PrepCommandId  { get; set; }
    public Guid   FulfilmentId   { get; set; }
    public string ContainersJson { get; set; } = "[]";

    public async Task<ExecutionResult> RunAsync(IStepExecutionContext context)
    {
        var containers = JsonSerializer.Deserialize<List<PreparationContainer>>(ContainersJson) ?? [];
        var payload    = new PreparationOutcomePayload(PrepCommandId, FulfilmentId, containers);
        var baseUrl    = config["Api:BaseUrl"] ?? throw new InvalidOperationException("Api:BaseUrl is not configured.");

        logger.LogInformation("[PreparationWorkflow] POST /api/preparation-outcome for {Id}", PrepCommandId);

        using var http = factory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        var response = await http.PostAsJsonAsync($"{baseUrl}/api/preparation-outcome", payload, context.CancellationToken);
        logger.LogInformation("[PreparationWorkflow] POST /api/preparation-outcome → {Status}", (int)response.StatusCode);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"POST /api/preparation-outcome returned {(int)response.StatusCode}");

        return ExecutionResult.Next();
    }
}
