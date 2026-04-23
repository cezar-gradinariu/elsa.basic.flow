namespace Elsa.Basic.Flow.Services.Preparation;

public interface IPreparationOutcomeHandler
{
    Task HandleAsync(PreparationOutcomePayload payload, CancellationToken ct = default);
}
