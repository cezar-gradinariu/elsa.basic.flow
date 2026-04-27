using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkflowCore.Basic.Flow.Services.Allocation;
using WorkflowCore.Basic.Flow.Services.Preparation.Commands;
using WorkflowCore.Interface;
using WorkflowCore.Models;

namespace WorkflowCore.Basic.Flow.Services.Fulfilment.Steps;

public class SendPrepareCommandsStep(
    IHttpClientFactory                  factory,
    IConfiguration                      config,
    ILogger<SendPrepareCommandsStep>    logger) : IStepBody
{
    // Inputs
    public Guid   FulfilmentId   { get; set; }
    public string AllocationJson { get; set; } = "";

    // Outputs
    public string PrepareCommandsJson { get; set; } = "[]";
    public int    TotalPreparations   { get; set; }

    public async Task<ExecutionResult> RunAsync(IStepExecutionContext context)
    {
        var result   = JsonSerializer.Deserialize<AllocationResult>(AllocationJson)
            ?? throw new InvalidOperationException("AllocationJson is empty or invalid");
        var baseUrl  = config["Api:BaseUrl"] ?? throw new InvalidOperationException("Api:BaseUrl is not configured.");

        var commands = result.StoreAllocations
            .Select(a => new PrepareCommand(Guid.NewGuid(), a.StoreId, a.Lines))
            .ToList();

        logger.LogInformation("[FulfilmentWorkflow] Dispatching {Count} preparation(s) for order {OrderNo}", commands.Count, result.OrderNo);

        using var http = factory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        foreach (var cmd in commands)
        {
            var payload = new
            {
                PrepCommandId = cmd.Id,
                FulfilmentId,
                cmd.StoreId,
                Lines = cmd.Lines
            };

            var delays = new[] { 2, 4, 8 };
            for (var attempt = 0; attempt <= delays.Length; attempt++)
            {
                try
                {
                    var response = await http.PostAsJsonAsync($"{baseUrl}/api/preparations", payload, context.CancellationToken);
                    response.EnsureSuccessStatusCode();
                    break;
                }
                catch (Exception ex) when (attempt < delays.Length && ex is not OperationCanceledException)
                {
                    logger.LogWarning("  → PrepareCommand {Id} POST failed (attempt {A}/{Max}), retrying in {D}s", cmd.Id, attempt + 1, delays.Length + 1, delays[attempt]);
                    await Task.Delay(TimeSpan.FromSeconds(delays[attempt]), context.CancellationToken);
                }
            }

            logger.LogInformation("  → PrepareCommand {Id}  store={StoreId}  lines={Lines} dispatched", cmd.Id, cmd.StoreId, cmd.Lines.Count);
        }

        PrepareCommandsJson = JsonSerializer.Serialize(commands);
        TotalPreparations   = commands.Count;
        return ExecutionResult.Next();
    }
}
