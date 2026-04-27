# WorkflowCore Port — Plan

Port of the Elsa-based `src/` solution to WorkflowCore (.NET), with MongoDB persistence.

---

## Solution Structure

```
src-workflowcore/
├── WorkflowCore.Basic.Flow.sln
└── src/
    ├── WorkflowCore.Basic.Flow.Api/
    ├── WorkflowCore.Basic.Flow.Domain/        ← identical domain model, no workflow deps
    ├── WorkflowCore.Basic.Flow.Services/      ← workflows, steps, commands, handlers
    └── WorkflowCore.Basic.Flow.Infrastructure/ ← MongoDB fulfilment repo, DI extensions
```

---

## Project Dependencies & NuGet Packages

| Project | Packages |
|---|---|
| **Domain** | none — pure .NET 10 |
| **Services** | `WorkflowCore`, `WorkflowCore.Persistence.MongoDB` |
| **Infrastructure** | `MongoDB.Driver` + refs to Domain + Services |
| **Api** | `Microsoft.AspNetCore.OpenApi`, `Swashbuckle.AspNetCore` + refs to Domain + Services + Infrastructure |

---

## How WorkflowCore Differs From Elsa (drives all design decisions)

| Concern | Elsa | WorkflowCore |
|---|---|---|
| Workflow state | `Properties` dict (manual seed workaround) | Strongly-typed `TData` class, auto-persisted to MongoDB |
| Sub-activities | `Activity` + `ExecuteAsync` | `IStepBody` + `RunAsync` returning `ExecutionResult` |
| Durable delay | `Elsa.Scheduling.Activities.Delay` | `ExecutionResult.Sleep(TimeSpan)` — durable with MongoDB persistence |
| Wait for external event | `CreateBookmark` + `EventStimulus` | `.WaitFor("EventName", key)` + `host.PublishEvent(...)` |
| Durable retry | Custom `DurableRetryActivity` base class | `OnError(WorkflowErrorHandling.Retry, TimeSpan)` per step + retry count in `TData` |
| Start a workflow | `IWorkflowRuntime.CreateInstanceAsync` + `IWorkflowDispatcher.DispatchAsync` | `IWorkflowHost.StartWorkflow(id, version, data)` |

The **TData** pattern is the biggest simplification: every piece of workflow state lives in a typed class that WorkflowCore persists automatically. No Properties seeding, no input-doesn't-survive-restart workaround.

---

## Domain Layer — `WorkflowCore.Basic.Flow.Domain`

Identical to the Elsa version. Copy verbatim:
- `FulfilmentAggregate.cs`
- `OrderLine.cs`, `PreparationContainer.cs`, `AllocatedLine.cs`
- `FulfilmentStatus.cs`, `ContainerType.cs`
- `IFulfilmentRepository.cs`
- `ConcurrencyException.cs`

---

## Services Layer — `WorkflowCore.Basic.Flow.Services`

File organisation follows the same feature-first + `Commands/` / `Workflows/` / `Steps/` convention (`Steps/` instead of `Activities/` — matches WorkflowCore's naming).

### Fulfilment Feature

**`FulfilmentWorkflowData.cs`**
```csharp
public class FulfilmentWorkflowData
{
    public Guid   FulfilmentId      { get; set; }
    public string OrderNo           { get; set; } = "";
    public string StoreId           { get; set; } = "";
    public string CustomerId        { get; set; } = "";
    public List<OrderLine> Lines    { get; set; } = [];

    // Written by AllocateFulfilmentStep
    public AllocationResult? AllocationResult { get; set; }
    public int AllocationAttempts             { get; set; }

    // Written by SendPrepareCommandsStep
    public List<PrepareCommand> PrepareCommands { get; set; } = [];
    public int TotalPreparations               { get; set; }

    // Incremented by WaitForPreparationsStep loop
    public int CompletedPreparations           { get; set; }
    public List<PreparationContainer> LatestContainers { get; set; } = [];
}
```

**`FulfilmentWorkflow.cs`** — implements `IWorkflow<FulfilmentWorkflowData>`

```
Id = "FulfilmentWorkflow", Version = 1

Sequence:
  AllocateFulfilmentStep          .OnError(Retry, 2s)
  SendPrepareCommandsStep         .OnError(Retry, 2s)
  While(data => data.CompletedPreparations < data.TotalPreparations)
    WaitFor("PreparationCompleted", data => data.FulfilmentId.ToString())
    .Output(data => data.LatestContainers, step => step.EventData)
    IncrementPreparationCountStep
  CompleteFulfilmentStep
```

**Steps:**

| Step | Maps from | Key logic |
|---|---|---|
| `AllocateFulfilmentStep` | `AllocateFulfilmentActivity` | POST `/api/allocations`. Increments `AllocationAttempts` in data; throws after 4 failures so `OnError(Retry)` terminates. Sets `data.AllocationResult`. |
| `SendPrepareCommandsStep` | `SendPrepareCommandsActivity` | POST `/api/preparations` × N. Writes `PrepareCommands` and `TotalPreparations` to data. |
| `IncrementPreparationCountStep` | Part of `WaitForPreparationsActivity` | Reads `LatestContainers` from event data, calls `/api/fulfilments/{id}/containers`, increments `CompletedPreparations`. |
| `CompleteFulfilmentStep` | `CompleteFulfilmentActivity` | Loads aggregate, calls `Complete()`, saves. |

No `InitialiseFulfilmentPropertiesActivity` equivalent needed — `TData` eliminates the need.

---

### Preparation Feature

**`PreparationWorkflowData.cs`**
```csharp
public class PreparationWorkflowData
{
    public Guid   PrepCommandId              { get; set; }
    public Guid   FulfilmentId               { get; set; }
    public string StoreId                    { get; set; } = "";
    public List<OrderLine> Lines             { get; set; } = [];
    public int    DelaySeconds               { get; set; }

    // Built once by LogPreparationStep, reused on retries
    public List<PreparationContainer> Containers { get; set; } = [];
    public int SendAttempts                       { get; set; }
}
```

**`PreparationWorkflow.cs`** — implements `IWorkflow<PreparationWorkflowData>`

```
Id = "PreparationWorkflow", Version = 1

Sequence:
  LogPreparationStep         (sets DelaySeconds, builds Containers into TData)
  Delay(data => TimeSpan.FromSeconds(data.DelaySeconds))
  SendPreparationOutcomeStep .OnError(Retry, 2s)
```

**Steps:**

| Step | Maps from | Key logic |
|---|---|---|
| `LogPreparationStep` | `LogPreparationRequestActivity` | Logs request, generates random delay (5–30 s), writes `data.DelaySeconds`. Pre-builds `data.Containers` here so it is idempotent on retry. |
| `SendPreparationOutcomeStep` | `CreateAndSendPreparationOutcomeActivity` | Reads `data.Containers` (already built). POST `/api/preparation-outcome` with `FulfilmentId` included. Increments `SendAttempts`; throws after 4 failures. |

`Delay` is WorkflowCore's built-in durable sleep — `ExecutionResult.Sleep(TimeSpan)` persisted to MongoDB, survives process restarts.

---

### Allocation Feature

`CreateAllocationCommand.cs`, `CreateAllocationHandler.cs`, `AllocationResult.cs`, `StoreAllocation.cs` — identical to the Elsa version, no workflow dependency.

---

### Event Naming Strategy

| Event name | Key | Published by | Consumed by |
|---|---|---|---|
| `"PreparationCompleted"` | `FulfilmentId.ToString()` | `POST /api/preparation-outcome` → `IWorkflowHost.PublishEvent` | `FulfilmentWorkflow` WaitFor loop |

`PreparationOutcomePayload` must carry `FulfilmentId` (the preparation workflow knows it via `TData.FulfilmentId`). `SendPreparationOutcomeStep` includes it in the payload.

---

### Retry Strategy

WorkflowCore's `OnError(WorkflowErrorHandling.Retry, TimeSpan)` retries at a fixed interval. To enforce a maximum attempt count:

- Each retryable step stores a counter in `TData` (`AllocationAttempts`, `SendAttempts`).
- The step increments the counter on each failure.
- After hitting max, the step throws a terminal exception — WorkflowCore faults the workflow.

This is less elegant than `DurableRetryActivity` but is standard WorkflowCore practice. True exponential backoff is not natively supported; a fixed 2 s retry interval is used.

---

## Infrastructure Layer — `WorkflowCore.Basic.Flow.Infrastructure`

**`MongoFulfilmentRepository.cs`** — identical implementation to the Elsa version (optimistic concurrency on `Version`, atomic `$inc` for `PrepCompletedCount`, `$set` not replace).

**`WorkflowCoreInfrastructureExtensions.cs`** — DI wiring:
```csharp
services.AddWorkflow(cfg =>
    cfg.UseMongoDB(connectionString, dbName,
        serializerTypeFilter: t => t.FullName?.StartsWith("WorkflowCore.Basic.Flow.") == true));

host.RegisterWorkflow<FulfilmentWorkflow>();
host.RegisterWorkflow<PreparationWorkflow>();
await host.Start();
```

No custom scheduler, no `SchedulerPollingService`, no `SchedulerWakeSignal`, no `RestartInterruptedWorkflowsTask` — WorkflowCore's MongoDB provider handles all of this natively.

---

## API Layer — `WorkflowCore.Basic.Flow.Api`

Same endpoints. Key differences in implementation:

| Endpoint | Elsa | WorkflowCore |
|---|---|---|
| `POST /api/fulfilments` | `IWorkflowRuntime` + `IWorkflowDispatcher` | `IWorkflowHost.StartWorkflow("FulfilmentWorkflow", 1, data)` |
| `POST /api/preparations` | same runtime pattern | `IWorkflowHost.StartWorkflow("PreparationWorkflow", 1, data)` |
| `POST /api/preparation-outcome` | `IPreparationOutcomeHandler` → `IStimulusSender` | `IWorkflowHost.PublishEvent("PreparationCompleted", fulfilmentId, containers)` |

`IPreparationOutcomeHandler` and `ElsaPreparationOutcomeHandler` are dropped entirely — `PublishEvent` is called directly in the endpoint handler.

---

## What Gets Dropped / Simplified

| Elsa component | Status in WorkflowCore port | Reason |
|---|---|---|
| `MongoWorkflowScheduler` | Dropped | WorkflowCore MongoDB provider handles scheduling |
| `SchedulerPollingService` | Dropped | WorkflowCore runs its own background polling |
| `SchedulerWakeSignal` | Dropped | Same |
| `MongoIndexInitialiser` | Dropped | WorkflowCore manages its own indexes |
| `InitialiseFulfilmentPropertiesActivity` | Dropped | `TData` is auto-persisted |
| `DurableRetryActivity` base class | Dropped | `OnError(Retry)` + attempt counter in `TData` |
| `IPreparationOutcomeHandler` + `ElsaPreparationOutcomeHandler` | Dropped | `PublishEvent` called directly |
| Complex `WorkflowInfrastructureExtensions` | Simplified to ~5 lines | WorkflowCore MongoDB setup is trivial |

---

## Open Questions Before Implementing

1. **Fault injection on `/api/allocations`**: Keep the existing 1-in-3 failure simulation for realistic retry testing?
2. **`PreparationOutcomePayload`**: Confirm adding `FulfilmentId` to this record is acceptable (required for event routing).
3. **WorkflowCore version**: 3.17.0 is current stable — any preference?
4. **Shared Domain project**: Copy the domain into the new solution, or reference the original as a shared project across both solutions?
