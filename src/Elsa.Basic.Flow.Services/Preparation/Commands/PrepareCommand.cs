using Elsa.Basic.Flow.Domain;

namespace Elsa.Basic.Flow.Services.Preparation.Commands;

public record PrepareCommand(Guid Id, string StoreId, List<OrderLine> Lines);
