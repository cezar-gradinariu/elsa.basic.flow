using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Basic.Flow.Services.Allocation;
using Elsa.Basic.Flow.Services.Preparation;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Elsa.Basic.Flow.Services.Fulfilment;

internal class SendPrepareCommandsActivity : Activity
{
    public Input<AllocationResult>?      AllocationResult { get; set; }
    public Input<string>?                FulfilmentId     { get; set; }
    public Output<List<PrepareCommand>>? PrepareCommands  { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var result       = context.Get(AllocationResult)!;
        var fulfilmentId = Guid.Parse(context.Get(FulfilmentId) ?? throw new InvalidOperationException("FulfilmentId is required"));
        var logger       = context.GetRequiredService<ILogger<SendPrepareCommandsActivity>>();
        var config       = context.GetRequiredService<IConfiguration>();
        var factory      = context.GetRequiredService<IHttpClientFactory>();
        var baseUrl      = config["Api:BaseUrl"] ?? throw new InvalidOperationException("Api:BaseUrl is not configured.");

        var commands = result.StoreAllocations
            .Select(a => new PrepareCommand(Guid.NewGuid(), a.StoreId, a.Lines))
            .ToList();

        logger.LogInformation("[FulfilmentWorkflow] Dispatching {Count} preparation(s) for order {OrderNo}", commands.Count, result.OrderNo);

        using var http = factory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        foreach (var cmd in commands)
        {
            var request = new
            {
                PrepCommandId = cmd.Id,
                FulfilmentId  = fulfilmentId,
                StoreId       = cmd.StoreId,
                Lines         = cmd.Lines
            };

            var delays = new[] { 2, 4, 8 };

            for (var attempt = 0; attempt <= delays.Length; attempt++)
            {
                try
                {
                    var response = await http.PostAsJsonAsync($"{baseUrl}/api/preparations", request, context.CancellationToken);
                    response.EnsureSuccessStatusCode();
                    break;
                }
                catch (Exception ex) when (attempt < delays.Length && ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "  → PrepareCommand id={Id} POST failed (attempt {Attempt}/{Max}), retrying in {Delay}s", cmd.Id, attempt + 1, delays.Length + 1, delays[attempt]);
                    await Task.Delay(TimeSpan.FromSeconds(delays[attempt]), context.CancellationToken);
                }
            }

            logger.LogInformation("  → PrepareCommand id={Id}  storeId={StoreId}  lines={Lines} dispatched", cmd.Id, cmd.StoreId, cmd.Lines.Count);
        }

        context.Set(PrepareCommands, commands);
        await context.CompleteActivityAsync();
    }
}
