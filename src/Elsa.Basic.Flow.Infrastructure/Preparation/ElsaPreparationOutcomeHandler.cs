using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Basic.Flow.Services.Preparation;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Stimuli;
using Microsoft.Extensions.Logging;

namespace Elsa.Basic.Flow.Infrastructure.Preparation;

public class ElsaPreparationOutcomeHandler(
    IStimulusSender stimulusSender,
    ILogger<ElsaPreparationOutcomeHandler> logger) : IPreparationOutcomeHandler
{
    public async Task HandleAsync(PreparationOutcomePayload payload, CancellationToken ct = default)
    {
        logger.LogInformation(
            "[PreparationOutcome] Received outcome for preparation {Id} ({Count} container(s))",
            payload.Id, payload.Containers.Count);

        foreach (var c in payload.Containers)
            logger.LogInformation(
                "  [{ContainerId}] {ContainerType}  lines={Lines}",
                c.ContainerId, c.ContainerType, c.AllocatedLines.Count);

        await stimulusSender.SendAsync(
            RuntimeStimulusNames.Event,
            new EventStimulus($"preparation-completed-{payload.Id}"),
            new StimulusMetadata
            {
                Input = new Dictionary<string, object>
                {
                    ["Containers"] = JsonSerializer.Serialize(payload.Containers)
                }
            },
            ct);

        logger.LogInformation("[PreparationOutcome] Signal sent for preparation {Id}", payload.Id);
    }
}
