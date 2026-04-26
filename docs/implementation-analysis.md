# Implementation Analysis Report

Last updated: 2026-04-26. Analysis of the Elsa.Basic.Flow solution against Elsa 3.6 best practices.

---

## CRITICAL

### 1. `MongoWorkflowScheduler` + `SchedulerPollingService` — Custom scheduler

**Status: KEPT — Elsa has no built-in MongoDB scheduler. Improved.**

Elsa 3.6 does **not** ship a MongoDB-backed `IWorkflowScheduler`. The default scheduler uses in-memory timers and loses all `Delay` state on process restart. The custom implementation is necessary for durability.

The custom code correctly:

- Registers task name → instance ID → bookmark ID → resume-at in MongoDB
- Upserts atomically on the same task name, so rescheduling is safe
- Uses `FindOneAndDeleteAsync` in the polling service to claim-and-remove atomically
- Catches `WorkflowInstanceNotFoundException` for stale tasks from previous runs

**Improvements made in this session:**

- `SchedulerWakeSignal` (SemaphoreSlim wrapper) added — shared between scheduler and polling service
- `SchedulerPollingService` now uses **next-task-aware sleep**: after draining overdue tasks it queries `MIN(ResumeAt)` from MongoDB and sleeps until that time, capped at 5 minutes
- `MongoWorkflowScheduler` calls `wakeSignal.Notify()` after scheduling, so the poller wakes immediately for new tasks
- Duplicate `[MongoScheduler] Unscheduled` log entries removed (Elsa calls `UnscheduleAsync` twice per task; the second call is a no-op and no longer logged)
- `ResumeAt` index added on `elsa_scheduled_tasks` (see #9 below)

Remaining minor concerns:

- Five unused overloads (`ScheduleNewWorkflowInstanceRequest`, `ScheduleRecurringAsync`, `ScheduleCronAsync`) throw `NotSupportedException`. Safe for Delay-only use; implement if cron/recurring workflows are added.
- Poll cap of 5 minutes is hard-coded — could be moved to `IConfiguration`.

**Alternative path (requires new packages):** `Elsa.Quartz` + a Quartz MongoDB job store would provide a fully managed durable scheduler.

---

### 2. `IPreparationCompletionTracker` + `MongoPreparationCompletionTracker` — Custom fan-in tracker

**Status: FIXED — removed, replaced with Elsa-native per-preparation bookmarks.**

The tracker maintained a separate `prep_tracking` MongoDB collection. Problems:

- Extra collection kept manually in sync with Elsa's own workflow state
- Race condition: preparation outcome could arrive before its bookmark was created → workflow hung forever
- Cleanup was not atomic; crash between signal and cleanup left orphaned tracker docs

**Replacement:** `WaitForPreparationsActivity` creates **N bookmarks** — one per preparation, each listening for `preparation-completed-{preparationId}`. Each callback atomically increments a counter in the **`fulfilments` MongoDB collection** via `repository.IncrementPrepCompletedAsync()` (MongoDB `$inc` operator, `FindOneAndUpdate` returning the updated document). The activity completes when the returned count equals the total. No external collection; no external fan-in logic.

> **Note:** The counter lives in the domain repository, not in `WorkflowExecutionContext.Properties`. This is intentional for atomicity — MongoDB `$inc` is race-safe under concurrent bookmark callbacks; a Properties-based read-modify-write would not be.

The `/api/preparation-outcome` endpoint sends a per-preparation stimulus (`preparation-completed-{id}`) targeting the fulfilment workflow instance directly by its Elsa instance ID (threaded through via `PrepareCommand.ParentWorkflowInstanceId`).

---

## MEDIUM

### 3. Manual `context.WorkflowExecutionContext.Input` extraction in activities

**Status: PARTIAL.** `FulfilmentId` wired properly in `SendPrepareCommandsActivity` using `Input<string>`. `AllocateFulfilmentActivity` and `LogPreparationRequestActivity` still use raw input-dict extraction — lower priority as they run first in their respective workflows where the input dict IS populated.

### 4. Race condition in original `WaitForPreparationsActivity`

**Status: FIXED** as part of Fix 2. Per-preparation bookmarks are created atomically in a single `ExecuteAsync` call; each stimulus targets exactly one bookmark.

### 5. No HTTP retry in `CreateAndSendPreparationOutcomeActivity`

**Status: FIXED (partially).** Added exponential-backoff retry (up to 4 attempts: 2 s, 4 s, 8 s) via a `for` loop with `await Task.Delay(…)`. After exhausting retries the activity throws, faulting the preparation workflow — visible in Elsa's `/elsa/api/workflow-instances`.

> **Resolved via #16:** Retry was subsequently replaced with the same durable bookmark + `IWorkflowScheduler` pattern used in `AllocateFulfilmentActivity`.

### 6. `SendPrepareCommandsActivity` serializes `OrderLines` to a JSON string

**Status: NOT FIXED — by design.**

Elsa deserializes workflow input values back to their original CLR types when a workflow resumes from persistence. Passing `List<OrderLine>` directly means `.ToString()` returns the CLR type name (not JSON), breaking `JsonSerializer.Deserialize`. The `JsonSerializer.Serialize()` pre-serialization is the correct workaround; removing it causes a runtime crash.

---

## LOW

### 7. `Console.WriteLine` used throughout instead of `ILogger<T>`

**Status: FIXED.**

- All activities now resolve `ILogger<T>` via `context.GetRequiredService<ILogger<T>>()`
- `MongoFulfilmentRepository` uses constructor-injected `ILogger<MongoFulfilmentRepository>`
- `MongoWorkflowScheduler` and `SchedulerPollingService` use constructor-injected loggers
- `FulfilmentAggregate` domain class: `Console.WriteLine` removed (domain should not log)
- `/api/preparation-outcome` endpoint: `Console.WriteLine` replaced with `ILogger<Program>`

### 8. `InMemoryPreparationCompletionTracker` — dead code

**Status: FIXED** — deleted with `IPreparationCompletionTracker.cs` as part of Fix 2.

### 9. No MongoDB indexes

**Status: FIXED.**

- `elsa_scheduled_tasks`: `{ ResumeAt: 1 }` — created at startup in `Program.cs` before `app.Run()`
- `fulfilments` collection relies on `_id` (auto-indexed by MongoDB); the optimistic-lock filter on `_id + Version` is covered by the `_id` index for the collection sizes expected here.

### 10. No input validation in `CreateAllocationHandler`

**Status: FIXED.** `CreateAllocationHandler` now throws `ArgumentException` early if `cmd.OrderLines` is empty, preventing a downstream divide-by-zero in the allocation logic.

---

## Second-pass findings (post-refactor)

### 11. Dead `prepareCommandId` parameter in `FulfilmentAggregate.UpdateContainers`

**Status: FIXED.** The `Console.WriteLine` calls that used `prepareCommandId` were removed in Fix 7; the parameter became dead. Removed from the method signature and the single call site in `Program.cs`.

### 12. Unsafe `Enum.Parse` in `MongoFulfilmentRepository`

**Status: FIXED.** `Enum.Parse<ContainerType>` and `Enum.Parse<FulfilmentStatus>` would throw on schema drift (unknown string stored in MongoDB). Replaced with `Enum.TryParse` with safe fallbacks (`ContainerType.Tote`, `FulfilmentStatus.Created`) in both `GetByIdAsync` and `LoadAsync`.

### 13. `KeyNotFoundException` risk in `WaitForPreparationsActivity` callback

**Status: FIXED.** `props[TotalKey]` / `props[CompletedKey]` would throw `KeyNotFoundException` if the callback replayed after a process crash before `ExecuteAsync` had persisted the counters. Replaced with `TryGetValue` with sensible defaults (`total = 0`, `completed = 1`).

### 14. Silent `Guid.Empty` fallback in `CreateAndSendPreparationOutcomeActivity`

**Status: FIXED.** `Guid.TryParse` falling back to `Guid.Empty` on a missing/invalid input meant the preparation outcome payload carried a zero GUID, silently corrupting the aggregate update. Replaced with `Guid.Parse` + explicit `InvalidOperationException`, which faults the workflow immediately with a clear message.

---

---

## Third-pass findings (2026-04-26)

### 15. In-process HTTP calls inside workflow activities

**Status: BY DESIGN.**

`AllocateFulfilmentActivity` and `SendPrepareCommandsActivity` call the local API over HTTP rather than invoking handlers directly. This is intentional — the design treats each API endpoint as a logical service boundary, even when co-hosted in the same process. Not a defect.

---

### 16. Non-durable retry in `CreateAndSendPreparationOutcomeActivity`

**Status: FIXED.**

Replaced the inline `for`-loop + `await Task.Delay(…)` with the same durable bookmark + `IWorkflowScheduler` pattern used in `AllocateFulfilmentActivity`:

- `ExecuteAsync` seeds `PrepareCommandId`, `LinesJson`, and the built `ContainersJson` into `WorkflowExecutionContext.Properties`, then calls `TrySendOutcomeAsync`.
- `TrySendOutcomeAsync` reads from Properties, attempts the HTTP POST, and on failure increments a retry counter, creates a durable bookmark, and schedules a MongoDB-backed `ScheduleExistingWorkflowInstanceRequest` via `IWorkflowScheduler`.
- The `SchedulerPollingService` fires the bookmark on restart, re-entering `TrySendOutcomeAsync` from Properties — survives process crash mid-retry.
- Containers are built once in `ExecuteAsync` and serialized to Properties so the same payload is sent on every retry (idempotent).

---

### 17. `AllocateFulfilmentActivity` — overly complex manual retry pattern

**Status: FIXED.**

The hand-rolled bookmark + `IWorkflowScheduler` retry loop has been replaced with a declarative `While` + built-in `Delay` pattern in `FulfilmentWorkflow`:

- `AllocateFulfilmentActivity` is now a single-attempt activity (~55 lines, down from ~90). It makes one HTTP call, sets `Succeeded` / `Result` / `FulfilmentId` / `NextDelay` outputs, increments a retry counter in Properties, and throws only on exhaustion.
- `FulfilmentWorkflow` wraps it in `new While(body) { Condition = new Input<bool>(ctx => !allocationSuccess.Get(ctx)) }` with `new If(!succeeded) { Then = new Delay(nextDelay) }` inside the loop body.
- Durability is preserved: `Delay` (built-in) hooks into the same MongoDB `IWorkflowScheduler` as before — no manual `CreateBookmark` or `ScheduleAtAsync` needed.
- All custom bookmark/scheduler plumbing (`CreateBookmark`, `ScheduleAtAsync`, `TryAllocateAsync` callback, `Elsa.Scheduling` import) removed from the activity.

Note: `new While(body) { ... }` constructor syntax required to disambiguate Elsa's overloaded `While` constructors.

---

### 18. `WaitForPreparationsActivity` couples workflow to domain repository

**Status: OPEN.**

The activity directly injects `IFulfilmentRepository` and calls `IncrementPrepCompletedAsync` to atomically track fan-in progress. This means the workflow activity has a hard dependency on the domain persistence layer.

**Concern:** The completion counter now lives in the `fulfilments` domain collection, not in Elsa's workflow state. This splits the "source of truth" for workflow progress across two stores — Elsa's `workflow_instances` and the domain `fulfilments` collection.

**Recommendation:** Keep the `$inc` atomicity (it's correct), but consider whether the counter belongs in a workflow-scoped document inside Elsa's own persistence (e.g., stored in `WorkflowExecutionContext.Properties` with conflict detection) or whether the domain aggregate is genuinely the right owner. If the former, Elsa's `Properties` survive restarts because they're serialized with the workflow instance — you'd need a different atomicity strategy (e.g., optimistic retry on the workflow dispatcher level).

---

### 19. `InitialiseFulfilmentPropertiesActivity` — purpose is unclear from code

**Status: OPEN.**

This activity exists solely to copy four values from `WorkflowExecutionContext.Input` into `WorkflowExecutionContext.Properties` before any other activity runs. The reason is durability: with `ExecutingActivityStrategy`, Input is committed before execution, but the code comment implies concern that Input could be lost on resume.

**Concern:** `ExecutingActivityStrategy` commits state (including Input) before each activity executes. The need for this extra first activity is not fully justified by Elsa's documented semantics, and it adds noise to the workflow definition.

**Recommendation:** Verify whether Elsa 3.6 with `ExecutingActivityStrategy` preserves Input across restarts without the copy. If it does, remove the activity and read from Input directly in the downstream activities. If Input is genuinely lost on resume (Elsa bug or design quirk), document this explicitly as a known Elsa limitation so future maintainers understand why the activity exists.

---

### 20. Two MongoDB databases — no transactional boundary between domain and Elsa state

**Status: OPEN.**

`CreateFulfilmentHandler` writes the domain aggregate to `fms.fulfilments`, then creates the Elsa workflow instance in `fms-workflows`. These are separate MongoDB databases; MongoDB multi-document transactions do not span databases.

If the workflow creation step fails after the aggregate is persisted, the aggregate is orphaned (exists in `fms` with no corresponding workflow). There is no compensating action.

**Recommendation:** Either:

- Consolidate Elsa and domain state into a single MongoDB database (simplest — no code change beyond connection strings, since both use the same MongoDB instance).
- Or accept the orphan risk and add a reconciliation job that detects aggregates with no corresponding workflow instance.

A single database also enables MongoDB multi-document transactions for true atomicity across aggregate creation and workflow dispatch.

---

## Summary

| # | Finding | Severity | Status |
|---|---------|----------|--------|
| 1 | Custom scheduler (Elsa has no MongoDB scheduler) | Critical | Kept — improved with smart sleep + wake signal |
| 2 | Custom tracker — per-preparation bookmarks | Critical | **Fixed** |
| 3 | Raw input dict extraction in activities | Medium | Partial |
| 4 | Race condition in WaitForPreparationsActivity | Medium | **Fixed** (via #2) |
| 5 | No HTTP retry in preparation outcome activity | Medium | Fixed with inline retry — non-durable (see #16) |
| 6 | OrderLines pre-serialized as JSON string | Medium | Not fixed — required by Elsa's persistence behaviour |
| 7 | Console.WriteLine instead of ILogger | Low | **Fixed** |
| 8 | InMemoryPreparationCompletionTracker dead code | Low | **Fixed** (removed) |
| 9 | No MongoDB indexes | Low | **Fixed** |
| 10 | No validation in CreateAllocationHandler | Low | **Fixed** |
| 11 | Dead `prepareCommandId` parameter | Low | **Fixed** |
| 12 | Unsafe `Enum.Parse` on schema drift | Medium | **Fixed** |
| 13 | KeyNotFoundException in bookmark callback | Medium | **Fixed** |
| 14 | Silent Guid.Empty fallback | Medium | **Fixed** |
| 15 | In-process HTTP calls inside workflow activities | Medium | By design |
| 16 | Non-durable retry in preparation outcome activity | Medium | **Fixed** |
| 17 | AllocateFulfilmentActivity — overly complex manual retry | Medium | **Fixed** |
| 18 | WaitForPreparationsActivity couples workflow to domain repository | Medium | **Open** |
| 19 | InitialiseFulfilmentPropertiesActivity — purpose unclear / may be unnecessary | Low | **Open** |
| 20 | Two MongoDB databases — no transactional boundary | Low | **Open** |
