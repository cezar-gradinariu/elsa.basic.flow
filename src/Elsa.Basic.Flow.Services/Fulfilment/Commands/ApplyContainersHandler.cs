using Elsa.Basic.Flow.Domain;
using Microsoft.Extensions.Logging;

namespace Elsa.Basic.Flow.Services.Fulfilment.Commands;

public class ApplyContainersHandler(
    IFulfilmentRepository             repository,
    ILogger<ApplyContainersHandler>   logger)
{
    public async Task HandleAsync(ApplyContainersCommand cmd, CancellationToken ct = default)
    {
        const int maxRetries = 5;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var aggregate = await repository.LoadAsync(cmd.FulfilmentId, ct);
                if (aggregate is null)
                {
                    logger.LogWarning("[ApplyContainers] Aggregate {FulfilmentId} not found", cmd.FulfilmentId);
                    return;
                }

                aggregate.UpdateContainers(cmd.Containers);
                await repository.SaveAsync(aggregate, ct);

                logger.LogInformation("[ApplyContainers] Aggregate {FulfilmentId} updated with {Count} container(s)",
                    cmd.FulfilmentId, cmd.Containers.Count);
                return;
            }
            catch (ConcurrencyException) when (attempt < maxRetries)
            {
                var delay = TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt - 1));
                logger.LogWarning("[ApplyContainers] Concurrency conflict (attempt {Attempt}/{Max}), retrying in {Delay}ms",
                    attempt, maxRetries, delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
        }
    }
}
