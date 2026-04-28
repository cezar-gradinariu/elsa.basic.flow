using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Stimuli;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Elsa.Basic.Flow.Services.Preparation.Activities;

internal class WaitForStorePreparationOutcomeActivity : Activity
{
    private const string ContainersKey = "Containers";

    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var props = context.WorkflowExecutionContext.Properties;
        props.TryGetValue(InitialiseStorePreparationPropertiesActivity.PrepCommandIdKey, out var pidRaw);
        var prepCommandId = pidRaw?.ToString() ?? Guid.Empty.ToString();

        context.GetRequiredService<ILogger<WaitForStorePreparationOutcomeActivity>>()
            .LogInformation("[StorePreparation] Waiting for outcome of prep={PrepId}", prepCommandId);

        context.CreateBookmark(new CreateBookmarkArgs
        {
            BookmarkName = RuntimeStimulusNames.Event,
            Stimulus     = new EventStimulus($"preparation-completed-{prepCommandId}"),
            Callback     = OnOutcomeReceivedAsync,
            AutoBurn     = true
        });

        return ValueTask.CompletedTask;
    }

    private async ValueTask OnOutcomeReceivedAsync(ActivityExecutionContext context)
    {
        var props = context.WorkflowExecutionContext.Properties;
        props.TryGetValue(InitialiseStorePreparationPropertiesActivity.PrepCommandIdKey, out var pidRaw);
        props.TryGetValue(InitialiseStorePreparationPropertiesActivity.FulfilmentIdKey,  out var fidRaw);

        var prepCommandId = pidRaw?.ToString() ?? Guid.Empty.ToString();
        var fulfilmentId  = fidRaw?.ToString() ?? string.Empty;

        var logger = context.GetRequiredService<ILogger<WaitForStorePreparationOutcomeActivity>>();

        // Containers are provided via stimulus metadata Input — ephemeral, only available during this callback.
        var input          = context.WorkflowExecutionContext.Input;
        var containersJson = input?.TryGetValue(ContainersKey, out var raw) == true ? raw?.ToString() : null;
        var containers     = containersJson is not null
            ? JsonSerializer.Deserialize<List<PreparationContainer>>(containersJson) ?? []
            : [];

        // Apply containers to the domain aggregate (best-effort — does not block completion).
        if (containers.Count > 0 && Guid.TryParse(fulfilmentId, out var fid))
        {
            var config  = context.GetRequiredService<IConfiguration>();
            var factory = context.GetRequiredService<IHttpClientFactory>();
            var baseUrl = config["Api:BaseUrl"] ?? "http://localhost:5000";

            using var http = factory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);

            try
            {
                var response = await http.PostAsJsonAsync(
                    $"{baseUrl}/api/fulfilments/{fid}/containers",
                    containers,
                    context.CancellationToken);
                response.EnsureSuccessStatusCode();
                logger.LogInformation("[StorePreparation] Applied {Count} container(s) to fulfilment {FulfilmentId}",
                    containers.Count, fid);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[StorePreparation] Failed to apply containers to fulfilment {FulfilmentId} — completion unaffected", fid);
            }
        }

        // Signal the parent FulfilmentWorkflow that this store preparation is done.
        var stimulusSender = context.GetRequiredService<IStimulusSender>();
        await stimulusSender.SendAsync(
            RuntimeStimulusNames.Event,
            new EventStimulus($"store-prep-completed-{prepCommandId}"),
            new StimulusMetadata(),
            context.CancellationToken);

        logger.LogInformation("[StorePreparation] prep={PrepId} complete — parent notified", prepCommandId);

        await context.CompleteActivityAsync();
    }
}
