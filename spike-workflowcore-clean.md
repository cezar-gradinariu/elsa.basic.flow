# SPIKE: Order Fulfilment Workflow — WorkflowCore Proof of Concept

**Type:** Spike (Time-boxed Research + Prototype)
**Story Points:** 5 (time-box: 2–3 days)
**Priority:** Medium
**Labels:** `spike`, `workflow`, `workflowcore`, `mongodb`, `dotnet10`

---

## Background

We need to orchestrate a durable, multi-step order fulfilment process that:

- Accepts an order with N line items and allocates them across 1–3 stores.
- Fans out to one preparation sub-workflow per store, running them independently.
- Waits for all store preparation outcomes before marking the fulfilment complete (fan-in).
- Tolerates process restarts at any point without losing workflow state or timers.
- Uses MongoDB as the sole persistence store — for both workflow state and the domain aggregate.

**Goal of this spike:** Evaluate [**WorkflowCore**](https://github.com/danielgerlag/workflow-core) (v3.17.0, by Daniel Gerlag) as the workflow engine for this scenario on **.NET 10** + **MongoDB** + **ASP.NET Core**. Produce a working skeleton and a verdict.

---

## What We Are Trying to Learn

1. Can WorkflowCore provide the durability guarantees we need (survive restarts mid-delay, mid-wait)?
2. How cleanly does the fan-out / fan-in pattern map to WorkflowCore primitives?
3. Does `WaitFor` / `PublishEvent` reliably handle out-of-order event arrival?
4. What infrastructure do we need to write ourselves vs. what WorkflowCore gives us out of the box?
5. What is the developer experience (verbosity, testability, local debugging)?

---

## Domain Model

**Aggregate:** `FulfilmentAggregate`
- `FulfilmentId` (Guid)
- `OrderNo`, `StoreNo`, `CustomerId` (string)
- `OrderLines` — list of `{ OrderNo, Sku, Quantity, UnitOfMeasure }`
- `Containers` — list of `PreparationContainer` objects added as each store completes
- `Status` — `Created` (only status for the spike)
- `Version` (int) — optimistic concurrency field; incremented on every save

**Optimistic concurrency:** `IFulfilmentRepository.SaveAsync()` upserts with a version filter. On version mismatch it throws `ConcurrencyException`; callers must retry with exponential backoff.

---

## Technology Stack

| Concern | Choice |
|---|---|
| Runtime | .NET 10, C# latest |
| Web framework | ASP.NET Core minimal API |
| Workflow engine | WorkflowCore 3.17.0 |
| Workflow persistence | WorkflowCore.Persistence.MongoDB 3.17.0 |
| Domain persistence | MongoDB (custom `fulfilments` collection) |
| MongoDB driver | MongoDB.Driver (latest stable) |

**NuGet packages to add** _(approval required per project rules)_:

| Package | Version |
|---|---|
| `WorkflowCore` | 3.17.0 |
| `WorkflowCore.Persistence.MongoDB` | 3.17.0 |

---

## Solution Structure

Mirror the standard 4-project layout:

```
Spike.WorkflowCore.sln
├── Spike.WorkflowCore.Domain/           # Aggregates, value objects, repository interfaces
├── Spike.WorkflowCore.Services/         # Workflow definitions, steps, command handlers
├── Spike.WorkflowCore.Infrastructure/   # MongoFulfilmentRepository, DI wiring
└── Spike.WorkflowCore.Api/              # ASP.NET Core endpoints, Program.cs
```

Group files within each project by feature first (`Fulfilment/`, `Preparation/`, `Allocation/`), then by kind (`Steps/`, `Workflows/`, `Commands/`).

---

## Workflow Implementations

### WorkflowCore Fundamentals

- **Workflows** implement `IWorkflow<TData>` and define their steps in `Build(IWorkflowBuilder<TData> builder)`.
- **Steps** inherit `StepBody` (sync) or `StepBodyAsync` (async). Public properties are inputs and outputs.
- **Persistence** is wired in `Program.cs`: `services.AddWorkflow(cfg => cfg.UseMongoDB(connStr, dbName))`. WorkflowCore manages its own MongoDB collections for workflow state, subscriptions, and events.
- **Starting a workflow:** `await host.StartWorkflow("WorkflowId", version: 1, data: inputData, correlationId: "...")`.
- **Sending an external event:** `await host.PublishEvent("EventName", eventKey, eventData)`.
- **Waiting for an event inside a workflow:** `.WaitFor("EventName", data => eventKey)`.

---

### FulfilmentWorkflow

**ID:** `"FulfilmentWorkflow"` **Version:** `1`

**Triggered by:** `POST /api/fulfilments` → `CreateFulfilmentHandler`
```
1. Create FulfilmentAggregate, save to MongoDB.
2. host.StartWorkflow("FulfilmentWorkflow", 1, data, correlationId: $"fulfilment-{fulfilmentId}")
3. Return 202 Accepted.
```

**Workflow data class:** `FulfilmentWorkflowData`

```text
FulfilmentId        : Guid
OrderNo             : string
StoreNo             : string
OrderLines          : List<OrderLine>
Allocation          : AllocationResult       // set by AllocateStep
ExpectedPrepCount   : int                    // set by FanOutPreparationStep; used to drive the fan-in loop
```

**Step sequence:**

```
AllocateStep
    └─► FanOutPreparationStep
            └─► [repeat ExpectedPrepCount times]
                    WaitFor("prep-completed", data => data.FulfilmentId.ToString())
                    └─► ApplyPreparationOutcomeStep
            └─► CompleteFulfilmentStep
```

#### Step Definitions

**`AllocateStep`** (`StepBodyAsync`)
- Calls `CreateAllocationHandler` with `OrderLines` and `StoreNo`.
- Handler distributes lines across 1–3 stores (round-robin; always includes the requested store).
- Writes `AllocationResult` into `FulfilmentWorkflowData.Allocation`.

**`FanOutPreparationStep`** (`StepBodyAsync`)
- Iterates `Allocation.StoreAllocations`.
- For each store, calls:
  ```csharp
  await host.StartWorkflow(
      "PreparationWorkflow", 1,
      new PreparationWorkflowData { PrepCommandId = Guid.NewGuid(), FulfilmentId = ..., StoreId = ..., Lines = ... },
      correlationId: $"preparation-{prepCommandId}");
  ```
- Sets `FulfilmentWorkflowData.ExpectedPrepCount = storeAllocations.Count`.
- Returns immediately — child workflows run independently.

**Fan-in block** (repeated `ExpectedPrepCount` times, max 3):

WorkflowCore's builder is static, so the fan-in is expressed as up to 3 conditional `.WaitFor` + step pairs. Each pair is skipped if `ExpectedPrepCount` is already satisfied (checked via `.When` condition on a branch). The event key is the `FulfilmentId` string — all prep outcomes for the same fulfilment publish to the same key; each `.WaitFor` consumes one event in arrival order.

```csharp
builder
    .StartWith<AllocateStep>(...)
    .Then<FanOutPreparationStep>(...)
    // fan-in: slot 1 (always runs — at least 1 store)
    .WaitFor("prep-completed", data => data.FulfilmentId.ToString())
    .Then<ApplyPreparationOutcomeStep>(...)
    // fan-in: slot 2 (runs only if ExpectedPrepCount >= 2)
    .If(data => data.ExpectedPrepCount >= 2)
        .Do(branch => branch
            .StartWith<NoOpStep>()
            .WaitFor("prep-completed", data => data.FulfilmentId.ToString())
            .Then<ApplyPreparationOutcomeStep>(...))
    // fan-in: slot 3 (runs only if ExpectedPrepCount == 3)
    .If(data => data.ExpectedPrepCount == 3)
        .Do(branch => branch
            .StartWith<NoOpStep>()
            .WaitFor("prep-completed", data => data.FulfilmentId.ToString())
            .Then<ApplyPreparationOutcomeStep>(...))
    .Then<CompleteFulfilmentStep>();
```

**`ApplyPreparationOutcomeStep`** (`StepBodyAsync`)
- Receives `containers` from the event data (mapped via `.Output()`).
- Loads `FulfilmentAggregate` via `IFulfilmentRepository.LoadAsync()`.
- Appends containers to the aggregate.
- Saves with optimistic lock; retries with exponential backoff on `ConcurrencyException`.

**`CompleteFulfilmentStep`** (`StepBodyAsync`)
- Logs fulfilment completion.
- May update aggregate status when a `Completed` status is introduced.

---

### PreparationWorkflow

**ID:** `"PreparationWorkflow"` **Version:** `1`

**Triggered by:** `FanOutPreparationStep` (see above).

**Workflow data class:** `PreparationWorkflowData`

```text
PrepCommandId   : Guid
FulfilmentId    : Guid
StoreId         : string
Lines           : List<OrderLine>
DelaySeconds    : int                        // set by LogPreparationStep
Containers      : List<PreparationContainer> // set by BuildContainersStep
```

**Step sequence:**

```
LogPreparationStep
    └─► Delay (built-in, durable)
            └─► BuildContainersStep
                    └─► SendOutcomeStep
```

#### Step Definitions

**`LogPreparationStep`** (`StepBodyAsync`)
- Logs preparation request (store, lines).
- Picks a random integer between 5 and 30 (seconds).
- Writes value into `PreparationWorkflowData.DelaySeconds`.

**`Delay`** (WorkflowCore built-in primitive)
```csharp
.Then<Delay>()
    .Input(step => step.Period, data => TimeSpan.FromSeconds(data.DelaySeconds))
```
- Delay state is persisted to MongoDB after this step begins.
- If the process dies mid-delay, WorkflowCore resumes from the persisted `PersistenceData` on restart — **no custom scheduler needed**.

**`BuildContainersStep`** (`StepBodyAsync`)
- Generates 1–3 `PreparationContainer` objects (random container types, lines allocated to each).
- Writes list into `PreparationWorkflowData.Containers`.

**`SendOutcomeStep`** (`StepBodyAsync`)
- HTTP POSTs to `POST /api/preparation-outcome` with `{ PrepCommandId, FulfilmentId, Containers }`.
- Retries with exponential backoff (up to 4 attempts) on non-2xx response or network failure.
- The API endpoint receives the POST and calls:
  ```csharp
  await host.PublishEvent(
      "prep-completed",
      payload.FulfilmentId.ToString(),
      payload.Containers);
  ```
  This wakes the parent `FulfilmentWorkflow` instance at its next `.WaitFor` slot.

---

## API Surface

| Method | Route | Purpose | Request | Response |
|---|---|---|---|---|
| `POST` | `/api/fulfilments` | Create fulfilment, start workflow | `{ orderNo, storeNo, customerId, orderLines[] }` | `202 Accepted`, `Location: /api/fulfilments/{id}` |
| `GET` | `/api/fulfilments/{id}` | Read fulfilment aggregate state | — | `FulfilmentAggregate` JSON |
| `POST` | `/api/allocations` | Allocate order lines across stores | `{ orderNo, storeId, orderLines[] }` | `AllocationResult` JSON |
| `POST` | `/api/preparation-outcome` | Receive prep result, signal parent workflow | `{ prepCommandId, fulfilmentId, containers[] }` | `200 OK` |

`POST /api/allocations` intentionally fails 33% of the time to exercise retry logic.

---

## Proposed Spike Scope

### In Scope

- [ ] **Solution skeleton** — 4-project layout, project references wired
- [ ] **Domain layer** — `FulfilmentAggregate`, `OrderLine`, `PreparationContainer`, `IFulfilmentRepository`, `ConcurrencyException`
- [ ] **WorkflowCore registration** — `services.AddWorkflow(cfg => cfg.UseMongoDB(...))` in `Program.cs`; both workflows registered and host started
- [ ] **`FulfilmentWorkflow`** — all steps + conditional fan-in implemented and runnable
- [ ] **`PreparationWorkflow`** — all steps including built-in `Delay` implemented and runnable
- [ ] **`MongoFulfilmentRepository`** — upsert with version filter, `ConcurrencyException` on mismatch
- [ ] **API endpoints** — all 4 routes wired to handlers
- [ ] **Durable delay test** — start a preparation workflow, kill the process during the delay, restart, confirm it resumes and completes
- [ ] **Race condition test** — publish `prep-completed` before the parent workflow reaches `.WaitFor`; record whether it is received or dropped

### Out of Scope

- Production-quality error handling beyond what is needed to observe the patterns
- Full allocation randomness (a simplified version is sufficient)
- Any workflow visualisation or designer tooling

---

## Acceptance Criteria

1. **End-to-end completion** — `POST /api/fulfilments` triggers `FulfilmentWorkflow`; all `PreparationWorkflow` instances complete; the parent workflow receives all outcomes and reaches `CompleteFulfilmentStep`.
2. **Process-restart durability** — killing and restarting the host mid-delay causes the preparation workflow to resume from persisted state and complete normally.
3. **Fan-in correctness** — when outcomes for 2 or 3 stores arrive in any order, the parent workflow waits for all of them before proceeding to `CompleteFulfilmentStep`.
4. **Race condition verdict** — the spike report documents clearly whether an event published before `.WaitFor` is registered is received or silently dropped, and what mitigation (if any) was applied.
5. **MongoDB only** — no in-memory persistence fallback; all workflow state and domain data lives in MongoDB.
6. **Findings recorded** — this file updated with: architecture decisions made, race condition result, code volume notes, and a proceed / do not proceed verdict with reasoning.

---

## Key Risks & Unknowns

| Risk | Likelihood | Impact | Mitigation to explore |
|---|---|---|---|
| `WaitFor` drops events published before the subscription is registered | Medium | High | Introduce a small delay between fan-out and event publish, or use WorkflowCore's `effectiveDate` parameter on `PublishEvent` |
| No native "wait for N events" primitive — static builder limits dynamic fan-in | High | Medium | Cap at 3 stores (matches allocation logic); use conditional `.If` branches for slots 2 and 3 |
| `.Parallel()` branches are single-threaded (not true concurrent) | High | Low | Not used in this design; fan-out is done by spawning independent child workflows |
| MongoDB driver version conflict between our driver and `WorkflowCore.Persistence.MongoDB` | Low | Medium | WorkflowCore targets MongoDB.Driver 2.19.x; verify compatibility before committing |
| WorkflowCore 3.17.0 runtime issues on .NET 10 | Low | High | Package targets .NET Standard 2.0 (forward-compatible); run smoke test as first task of spike |

---

## Definition of Done

- Spike branch `features/spike-workflowcore` pushed and builds clean
- Both workflows execute end-to-end at least once against a local MongoDB instance
- Durable delay test result recorded in this file
- Race condition test result recorded in this file
- This file updated with verdict and recommended next steps

---

## References

- [WorkflowCore GitHub](https://github.com/danielgerlag/workflow-core)
- [WorkflowCore Docs](https://workflow-core.readthedocs.io/)
- [WorkflowCore.Persistence.MongoDB NuGet](https://www.nuget.org/packages/WorkflowCore.Persistence.MongoDB)
- [External events — WorkflowCore docs](https://workflow-core.readthedocs.io/en/latest/external-events/)
- [Control structures — WorkflowCore docs](https://workflow-core.readthedocs.io/en/latest/control-structures/)
- [Known issue: WaitFor race condition #952](https://github.com/danielgerlag/workflow-core/issues/952)
- [Known issue: Parallel steps single-threaded #793](https://github.com/danielgerlag/workflow-core/issues/793)
