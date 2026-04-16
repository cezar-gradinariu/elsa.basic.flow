using System.Text.Json;
using Elsa.Basic.Flow.Domain;
using Elsa.Basic.Flow.Services.Preparation;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Basic.Flow.Services.Fulfilment;

internal class UpdateAggregateWithContainersActivity : Activity
{
    public Input<string>? FulfilmentIdIn { get; set; }
    public Input<string>? PrepareCommandIdIn { get; set; }
    public Input<string>? PreparationOutcomeIn { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            // Get data from workflow variables
            var fulfilmentId = context.Get(FulfilmentIdIn) ?? "";
            var prepareCommandId = context.Get(PrepareCommandIdIn) ?? "";
            var outcomeJson = context.Get(PreparationOutcomeIn) ?? "";

            Console.WriteLine($"[UpdateAggregateWithContainers] Starting update:");
            Console.WriteLine($"  FulfilmentId: {fulfilmentId}");
            Console.WriteLine($"  PrepareCommandId: {prepareCommandId}");

            if (!Guid.TryParse(fulfilmentId, out var fulfilmentGuid))
            {
                Console.WriteLine($"[UpdateAggregateWithContainers] ✗ Invalid FulfilmentId format: {fulfilmentId}");
                await context.CompleteActivityAsync();
                return;
            }

            if (!Guid.TryParse(prepareCommandId, out var prepareCommandGuid))
            {
                Console.WriteLine($"[UpdateAggregateWithContainers] ✗ Invalid PrepareCommandId format: {prepareCommandId}");
                await context.CompleteActivityAsync();
                return;
            }

            // Deserialize preparation outcome
            var outcome = JsonSerializer.Deserialize<PreparationOutcomePayload>(outcomeJson);
            if (outcome == null)
            {
                Console.WriteLine($"[UpdateAggregateWithContainers] ✗ Failed to deserialize preparation outcome");
                await context.CompleteActivityAsync();
                return;
            }

            Console.WriteLine($"[UpdateAggregateWithContainers] Parsed outcome with {outcome.Containers.Count} containers");

            // Get repository from DI
            var sp = context.WorkflowExecutionContext.ServiceProvider;
            var repository = sp.GetRequiredService<IFulfilmentRepository>();

            // Retry loop for optimistic concurrency handling
            const int maxRetries = 5;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Load aggregate
                    Console.WriteLine($"[UpdateAggregateWithContainers] Loading aggregate {fulfilmentGuid}... (attempt {attempt})");
                    var aggregate = await repository.LoadAsync(fulfilmentGuid);
                    if (aggregate == null)
                    {
                        Console.WriteLine($"[UpdateAggregateWithContainers] ✗ Aggregate not found: {fulfilmentGuid}");
                        await context.CompleteActivityAsync();
                        return;
                    }

                    Console.WriteLine($"[UpdateAggregateWithContainers] Loaded aggregate {aggregate.OrderNo} (Version: {aggregate.Version})");

                    // Update containers
                    aggregate.UpdateContainers(outcome.Containers, prepareCommandGuid);

                    // Save aggregate
                    Console.WriteLine($"[UpdateAggregateWithContainers] Saving updated aggregate...");
                    await repository.SaveAsync(aggregate);

                    Console.WriteLine($"[UpdateAggregateWithContainers] ✓ Aggregate updated and saved successfully");
                    break; // Success - exit retry loop
                }
                catch (ConcurrencyException ex) when (attempt < maxRetries)
                {
                    // Exponential backoff: 100ms, 200ms, 400ms, 800ms
                    var delay = TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt - 1));
                    Console.WriteLine($"[UpdateAggregateWithContainers] ⏳ Concurrency conflict (attempt {attempt}/{maxRetries}): {ex.Message}");
                    Console.WriteLine($"[UpdateAggregateWithContainers] ⏳ Retrying in {delay.TotalMilliseconds}ms...");
                    
                    await Task.Delay(delay);
                }
                catch (ConcurrencyException ex) when (attempt == maxRetries)
                {
                    Console.WriteLine($"[UpdateAggregateWithContainers] ✗ Failed after {maxRetries} attempts: {ex.Message}");
                    throw; // Re-throw on final attempt
                }
            }

            await context.CompleteActivityAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UpdateAggregateWithContainers] ✗ Error updating aggregate: {ex.Message}");
            Console.WriteLine($"[UpdateAggregateWithContainers] Stack trace: {ex.StackTrace}");
            throw;
        }
    }
}