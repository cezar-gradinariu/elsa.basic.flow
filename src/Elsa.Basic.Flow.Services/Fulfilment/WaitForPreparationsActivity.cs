using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Basic.Flow.Services.Preparation;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Stimuli;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Elsa.Basic.Flow.Services.Fulfilment;

internal class WaitForPreparationsActivity : Activity
{
    private const string TotalKey      = "_prep_total";
    private const string CompletedKey  = "_prep_completed";
    private const string FulfilmentKey = "_prep_fulfilment_id";
    private const string ContainersKey = "Containers";

    public Input<List<PrepareCommand>>? PrepareCommands { get; set; }
    public Input<string>?               FulfilmentId    { get; set; }

    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var commands     = context.Get(PrepareCommands)!;
        var fulfilmentId = context.Get(FulfilmentId) ?? string.Empty;
        var props        = context.WorkflowExecutionContext.Properties;

        props[TotalKey]      = commands.Count;
        props[CompletedKey]  = 0;
        props[FulfilmentKey] = fulfilmentId;

        context.GetRequiredService<ILogger<WaitForPreparationsActivity>>()
            .LogInformation("[FulfilmentWorkflow] Registered {Count} preparation(s), suspending until all complete", commands.Count);

        foreach (var cmd in commands)
        {
            context.CreateBookmark(new CreateBookmarkArgs
            {
                BookmarkName = RuntimeStimulusNames.Event,
                Stimulus     = new EventStimulus($"preparation-completed-{cmd.Id}"),
                Callback     = OnPreparationCompletedAsync,
                AutoBurn     = true
            });
        }

        return ValueTask.CompletedTask;
    }

    private async ValueTask OnPreparationCompletedAsync(ActivityExecutionContext context)
    {
        var props        = context.WorkflowExecutionContext.Properties;
        var total        = props.TryGetValue(TotalKey,      out var t) ? Convert.ToInt32(t) : 0;
        var completed    = props.TryGetValue(CompletedKey,  out var c) ? Convert.ToInt32(c) + 1 : 1;
        var fulfilmentId = props.TryGetValue(FulfilmentKey, out var f) ? f?.ToString() ?? string.Empty : string.Empty;
        props[CompletedKey] = completed;

        var logger = context.GetRequiredService<ILogger<WaitForPreparationsActivity>>();
        logger.LogInformation("[FulfilmentWorkflow] Preparation completed ({Completed}/{Total})", completed, total);

        var input          = context.WorkflowExecutionContext.Input;
        var containersJson = input?.TryGetValue(ContainersKey, out var raw) == true ? raw?.ToString() : null;
        var containers     = containersJson is not null
            ? JsonSerializer.Deserialize<List<PreparationContainer>>(containersJson) ?? []
            : [];

        if (containers.Count > 0 && Guid.TryParse(fulfilmentId, out var fId))
        {
            var config  = context.GetRequiredService<IConfiguration>();
            var factory = context.GetRequiredService<IHttpClientFactory>();
            var baseUrl = config["Api:BaseUrl"] ?? "http://localhost:5000";

            using var http = factory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);

            var response = await http.PostAsJsonAsync(
                $"{baseUrl}/api/fulfilments/{fId}/containers",
                containers,
                context.CancellationToken);

            response.EnsureSuccessStatusCode();

            logger.LogInformation("[FulfilmentWorkflow] Applied {Count} container(s) to fulfilment {FulfilmentId}", containers.Count, fId);
        }

        if (completed >= total)
        {
            logger.LogInformation("[FulfilmentWorkflow] All preparations completed — continuing workflow");
            await context.CompleteActivityAsync();
        }
    }
}
