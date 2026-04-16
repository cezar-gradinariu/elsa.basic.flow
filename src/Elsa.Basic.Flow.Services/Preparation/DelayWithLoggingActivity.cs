using Elsa.Workflows;

namespace Elsa.Basic.Flow.Services.Preparation;

internal class DelayWithLoggingActivity : Activity
{
    public int DelaySeconds { get; set; } = 10;
    public string Phase { get; set; } = "start"; // "start" or "end"

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            if (Phase == "start")
            {
                Console.WriteLine($"[PreparationWorkflow] Starting delay of {DelaySeconds} seconds...");
            }
            else
            {
                Console.WriteLine($"[PreparationWorkflow] Delay of {DelaySeconds} seconds completed - building outcome payload");
            }
            
            await context.CompleteActivityAsync();
        }
        catch (ObjectDisposedException ex)
        {
            Console.WriteLine($"[PreparationWorkflow] Service disposed during DelayLogging execution: {ex.ObjectName}");
            // Don't re-throw - the logging was completed
        }
        catch (Exception ex) when (ex.ToString().Contains("IServiceProvider"))
        {
            Console.WriteLine($"[PreparationWorkflow] Service provider disposed during DelayLogging: {ex.Message}");
            // Don't re-throw - the logging was completed
        }
    }
}