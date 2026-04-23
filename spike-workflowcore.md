# SPIKE: Order Fulfilment Workflow — WorkflowCore Proof of Concept

**Type:** Spike (Time-boxed Research + Prototype)
**Story Points:** 5 (time-box: 2–3 days)
**Priority:** Medium
**Labels:** `spike`, `workflow`, `workflowcore`, `mongodb`, `dotnet10`

---

## Background

We have an existing order fulfilment solution built on **Elsa Workflows 3.6** + **MongoDB** + **.NET 10**.
The solution orchestrates:
- A `FulfilmentWorkflow` that allocates an order across 1–3 stores and fans out to parallel `PreparationWorkflow` instances.
- `PreparationWorkflow` instances that simulate store preparation (random 5–30 second delay) and POST results back to the API.
- The parent workflow waits for _all_ preparation outcomes before completing (fan-in via Elsa bookmarks/stimuli).
- Durable timers that survive process restarts (custom `MongoWorkflowScheduler` + `SchedulerPollingService`).
- Optimistic concurrency on the `FulfilmentAggregate` (version field, retry on conflict).

**Goal of this spike:** Determine whether the same scenario can be implemented with [**WorkflowCore**](https://github.com/danielgerlag/workflow-core) (by Daniel Gerlag) using the same stack (.NET 10, MongoDB, ASP.NET Core), identify risks, and produce a working skeleton or verdict.

---

## What We Are Trying to Learn

1. Can WorkflowCore replace Elsa in this scenario with comparable durability guarantees?
2. How does the fluent step API map to our current Elsa activities?
3. How does `WaitFor` / `PublishEvent` compare to Elsa bookmarks/stimuli for the fan-in pattern?
4. Does durable delay (simulated preparation time) survive a process restart with the MongoDB persistence provider?
5. What code and infrastructure do we need to write that Elsa gave us for free?
6. What is the developer experience difference (verbosity, testability, debugging)?

---

## Reference — Current Elsa Implementation Summary

| Concern | Elsa Approach |
|---|---|
| Workflow definition | `IWorkflowBuilder` fluent API, activity classes inherit `Activity` |
| Persistence | `UseMongoDb()` — Elsa manages its own collections; custom `fulfilments` collection for domain aggregate |
| Durable delay | Custom `MongoWorkflowScheduler` + `SchedulerPollingService` background service |
| External event (fan-in) | `context.CreateBookmark(new CreateBookmarkArgs { Stimulus = new EventStimulus("...") })` |
| Signal delivery | `IStimulusSender.SendAsync("Elsa.Event", new EventStimulus("..."), metadata)` |
| Workflow trigger | `IWorkflowRuntime.CreateInstanceAsync()` + `IWorkflowDispatcher.DispatchAsync()` |
| Correlation | `correlationId: $"fulfilment-{fulfilmentId}"` per workflow instance |
| Fan-out | `SendPrepareCommandsActivity` spawns N child `PreparationWorkflow` instances |
| Fan-in | `WaitForPreparationsActivity` creates N bookmarks; callback decrements counter, updates aggregate |
| Retry on concurrency | Exponential backoff in bookmark callback when `ConcurrencyException` is thrown |

---

## WorkflowCore Mapping (Research Findings)

| Concern | WorkflowCore Approach | Gap / Risk |
|---|---|---|
| Workflow definition | `IWorkflow<TData>` interface + `.Build(IWorkflowBuilder<TData>)` fluent API | Step classes must inherit `StepBody`/`StepBodyAsync` — more boilerplate than Elsa |
| Persistence | `services.AddWorkflow(cfg => cfg.UseMongoDB(connStr, dbName))` via `WorkflowCore.Persistence.MongoDB` NuGet | Straightforward; same MongoDB |
| Durable delay | Built-in `Delay` primitive; state persisted to MongoDB; resumes on next poll | No custom scheduler needed — Elsa required one |
| External event | `.WaitFor("EventName", data => correlationKey)` / `host.PublishEvent("EventName", key, data)` | **Race condition risk**: events published before `WaitFor` is reached may be lost (known issue #952, #1040) |
| Workflow trigger | `host.StartWorkflow("WorkflowId", version, inputData, correlationId)` | Correlation ID enforced unique — idempotent start built-in |
| Correlation | `correlationId` param on `StartWorkflow`; query back via `GetWorkflowsByCorrelationId` | Same concept, simpler API |
| Fan-out | Custom step spawns N child workflows via `host.StartWorkflow(...)` | Same pattern; child workflows run independently |
| Fan-in | Parent uses `.WaitFor("prep-completed", data => prepId)` N times, or a counter approach | No native "wait for N events" — needs design (counter in workflow data + loop/jump) |
| Parallel branches | `.Parallel().Do(...).Do(...).Join()` | **Single-threaded**: branches execute sequentially on same thread; not true concurrency |
| Step inputs/outputs | Public properties on step class; `.Input(s => s.Prop, d => d.Value).Output(...)` | More verbose mapping than Elsa's `ActivityInput`/`ActivityOutput` attributes |

---

## Workflow Implementations

### FulfilmentWorkflow

**Trigger:** `POST /api/fulfilments` → `CreateFulfilmentHandler` → `host.StartWorkflow("FulfilmentWorkflow", 1, data, correlationId: $"fulfilment-{fulfilmentId}")`

**Workflow data class:** `FulfilmentWorkflowData`

```text
FulfilmentId        : Guid
OrderNo             : string
StoreNo             : string
OrderLines          : List<OrderLine>
Allocation          : AllocationResult       // populated by AllocateStep
PendingPrepCount    : int                    // countdown for fan-in
```

**Steps (in order):**

1. **`AllocateStep`** (`StepBodyAsync`)
   - Calls `CreateAllocationHandler` to split `OrderLines` across 1–3 stores.
   - Writes result into `FulfilmentWorkflowData.Allocation`.

2. **`FanOutPreparationStep`** (`StepBodyAsync`)
   - Iterates `Allocation.StoreAllocations`.
   - For each store: calls `host.StartWorkflow("PreparationWorkflow", 1, prepData, correlationId: $"preparation-{prepCommandId}")`.
   - Sets `FulfilmentWorkflowData.PendingPrepCount = N`.
   - _Note: child workflows run independently; the parent does not block here._

3. **Fan-in loop** — repeated N times (once per store allocation), each iteration:
   - `.WaitFor("prep-completed", data => data.FulfilmentId.ToString())`
   - **`ApplyPreparationOutcomeStep`** (`StepBodyAsync`)
     - Reads containers from event data.
     - Loads `FulfilmentAggregate` via `IFulfilmentRepository.LoadAsync()`.
     - Appends containers to aggregate, saves with optimistic lock + exponential backoff retry on `ConcurrencyException`.
     - Decrements `PendingPrepCount`.
   - _The `.WaitFor` key is the fulfilment ID; all prep outcomes for this fulfilment publish to the same key. Each `.WaitFor` consumes one event._

4. **`CompleteFulfilmentStep`** (`StepBodyAsync`)
   - Logs completion.
   - Optionally sets aggregate status.

**Fan-in design choice — Option A (N sequential `WaitFor` calls):**
The number of stores N is known after allocation. The builder cannot loop dynamically, so N is bounded (max 3). The workflow definition uses `.WaitFor` three times with a conditional skip if `PendingPrepCount` is already 0 — making it handle 1, 2, or 3 stores correctly.

---

### PreparationWorkflow

**Trigger:** Spawned by `FanOutPreparationStep` via `host.StartWorkflow("PreparationWorkflow", 1, data, correlationId: $"preparation-{prepCommandId}")`

**Workflow data class:** `PreparationWorkflowData`

```text
PrepCommandId   : Guid
FulfilmentId    : Guid
StoreId         : string
Lines           : List<OrderLine>
DelaySeconds    : int       // populated by LogPreparationStep
Containers      : List<PreparationContainer>   // populated by SendOutcomeStep
```

**Steps (in order):**

1. **`LogPreparationStep`** (`StepBodyAsync`)
   - Logs prep request details.
   - Generates a random delay between 5 and 30 seconds.
   - Writes value into `PreparationWorkflowData.DelaySeconds`.

2. **`Delay`** (built-in WorkflowCore primitive)
   - `.Input(step => step.Period, data => TimeSpan.FromSeconds(data.DelaySeconds))`
   - State persisted to MongoDB; survives process restart.

3. **`BuildContainersStep`** (`StepBodyAsync`)
   - Generates 1–3 random `PreparationContainer` objects for the store's lines.
   - Writes into `PreparationWorkflowData.Containers`.

4. **`SendOutcomeStep`** (`StepBodyAsync`)
   - POSTs to `POST /api/preparation-outcome` with `{ PrepCommandId, FulfilmentId, Containers }`.
   - Uses exponential backoff (up to 4 attempts) — same as the Elsa implementation.
   - _The API endpoint then calls `host.PublishEvent("prep-completed", fulfilmentId.ToString(), containers)`._

---

## Proposed Spike Scope

### In Scope

- [ ] **Project skeleton** — new .NET 10 solution mirroring the 4-project layout (Domain, Services, Infrastructure, Api)
- [ ] **Domain layer** — reuse `FulfilmentAggregate`, `OrderLine`, `PreparationContainer` as-is (no changes)
- [ ] **WorkflowCore wiring** — register `WorkflowCore` + MongoDB persistence in `Program.cs`
- [ ] **`FulfilmentWorkflow`** — implement all 4 steps + fan-in loop as described above
- [ ] **`PreparationWorkflow`** — implement all 4 steps as described above
- [ ] **External event endpoint** — `POST /api/preparation-outcome` calls `host.PublishEvent("prep-completed", fulfilmentId, containers)`
- [ ] **Durable delay validation** — start a preparation workflow, kill the process mid-delay, restart, verify it resumes
- [ ] **Race condition test** — deliberately publish event before `WaitFor` is reached; document whether it is received
- [ ] **MongoDB aggregate persistence** — reuse `MongoFulfilmentRepository` with optimistic lock retry
- [ ] **API surface** — `POST /api/fulfilments`, `GET /api/fulfilments/{id}`, `POST /api/preparation-outcome`

### Out of Scope

- Full feature parity with the Elsa implementation
- Production-quality error handling or retry policies
- Allocation randomness / multi-store logic beyond what is needed to exercise the workflow

---

## Acceptance Criteria

1. **Workflow completes end-to-end** — `POST /api/fulfilments` triggers `FulfilmentWorkflow`; all `PreparationWorkflow` instances complete; parent workflow receives all outcomes and reaches its terminal step.
2. **Process-restart durability** — kill and restart the host mid-delay; the preparation workflow resumes the delay from persisted state and completes.
3. **Fan-in correctness** — when 3 preparation outcomes arrive in any order, the parent workflow proceeds only after all 3 are received.
4. **Race condition verdict documented** — the spike report states clearly whether the known `WaitFor` race condition affects this scenario and, if so, what mitigation was applied.
5. **MongoDB is the only persistence** — no in-memory fallback; MongoDB failure causes the workflow to stop, not silently lose state.
6. **Spike report** — this `.md` file is updated with:
   - Final architecture decisions
   - Step-by-step mapping table (Elsa activity → WorkflowCore step)
   - Code volume comparison (lines of code, number of files)
   - Verdict: proceed / do not proceed, and why

---

## Key Risks & Unknowns

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| `WaitFor` race condition drops preparation outcomes | Medium | High | Publish events with a slight delay after workflow start, or poll-and-retry the event publish |
| Fan-in "wait for N" has no native primitive | High | Medium | Design with a counter in workflow data + conditional branch; prototype before committing |
| Parallel branches are single-threaded | High | Low (here) | In this scenario branches are I/O-bound and sequential is acceptable; document the limitation |
| MongoDB driver version conflict with `WorkflowCore.Persistence.MongoDB` | Low | Medium | Package targets MongoDB.Driver 2.19.x; verify no conflict with our driver version |
| WorkflowCore 3.17 + .NET 10 runtime issues | Low | High | Targets .NET Standard 2.0 — forward-compatible; run a smoke test first |

---

## NuGet Packages to Evaluate

> **Per project rules: get explicit approval before adding any new package.**

| Package | Version | Purpose |
|---|---|---|
| `WorkflowCore` | 3.17.0 | Core workflow engine |
| `WorkflowCore.Persistence.MongoDB` | 3.17.0 | MongoDB persistence provider |

No other new packages anticipated — the rest of the stack (ASP.NET Core, MongoDB.Driver) is already in use.

---

## Definition of Done

- Spike branch pushed to `features/spike-workflowcore`
- At least Options A and B for fan-in have been coded (even if partially)
- Process-restart durability test executed and result recorded in this file
- This `.md` updated with findings, verdict, and recommended next steps
- Brief demo walkthrough recorded or presented to the team (optional but preferred)

---

## References

- [WorkflowCore GitHub](https://github.com/danielgerlag/workflow-core)
- [WorkflowCore Docs](https://workflow-core.readthedocs.io/)
- [WorkflowCore.Persistence.MongoDB NuGet](https://www.nuget.org/packages/WorkflowCore.Persistence.MongoDB)
- [Known issue: WaitFor race condition #952](https://github.com/danielgerlag/workflow-core/issues/952)
- [Known issue: Parallel steps single-threaded #793](https://github.com/danielgerlag/workflow-core/issues/793)
- Current Elsa implementation: `c:\Users\1230075\wxRepos\Elsa.Basic.Flow`
