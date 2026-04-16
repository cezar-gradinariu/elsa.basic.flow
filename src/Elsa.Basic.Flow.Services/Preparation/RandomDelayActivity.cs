using Elsa.Workflows;

namespace Elsa.Basic.Flow.Services.Preparation;

internal class RandomDelayActivity : Activity
{
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var seconds = Random.Shared.Next(5, 31);
        Console.WriteLine($"[PreparationWorkflow] Sleeping for {seconds}s…");
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        Console.WriteLine("[PreparationWorkflow] Awake — building outcome payload.");
        await context.CompleteActivityAsync();
    }
}
