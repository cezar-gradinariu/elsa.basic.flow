namespace Elsa.Basic.Flow.Domain;

public class ConcurrencyException(Guid fulfilmentId, int expectedVersion)
    : Exception($"Concurrency conflict on fulfilment {fulfilmentId}: expected version {expectedVersion} was already superseded.");
