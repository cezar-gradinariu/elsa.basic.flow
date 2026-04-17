# Implementation Analysis Report

Generated: 2026-04-17. Analysis of the Elsa.Basic.Flow solution against Elsa 3.6 best practices.

---

## CRITICAL

### 1. `MongoWorkflowScheduler` + `SchedulerPollingService` — Custom scheduler

**Status: KEPT — Elsa has no built-in MongoDB scheduler.**

The analysis initially flagged these as duplicating Elsa's built-in. After investigation, Elsa 3.6 does **not** ship a MongoDB-backed `IWorkflowScheduler`. The default scheduler uses in-memory timers and loses all Delay state on restart. The custom implementation is therefore necessary for durability.

The custom code correctly:
- Registers the task name → instance ID → bookmark ID → resume-at mapping in MongoDB
- Atomically replaces (upsert) on the same task name, so rescheduling is safe
- Uses `FindOneAndDeleteAsync` in the polling service to claim-and-remove atomically
- Catches `WorkflowInstanceNotFoundException` for stale tasks from previous runs

Remaining minor concerns (not yet actioned):
- The five unused overloads (`ScheduleNewWorkflowInstanceRequest`, `ScheduleRecurringAsync`, `ScheduleCronAsync`) throw `NotSupportedException`. Safe for the current Delay-only use case; would need implementing if cron/recurring workflows are ever added.
- Hard-coded 1-second poll interval in `SchedulerPollingService` — could be made configurable via `IConfiguration`.

**Alternative path (requires new packages):** `Elsa.Quartz` + a Quartz MongoDB job store would provide a fully managed durable scheduler. Ask the user before adding packages.

---

### 2. `IPreparationCompletionTracker` + `MongoPreparationCompletionTracker` — Custom fan-in tracker

**Status: REMOVED — replaced with Elsa-native per-preparation bookmarks.**

The tracker maintained a separate `prep_tracking` MongoDB collection to:
1. Know which workflow instance to signal (given only a preparation ID)
2. Count completions and fire the signal only when all N preparations finished

**Problems with the tracker:**
- Extra collection in `fms-workflows` kept manually in sync with Elsa's own workflow state
- Race condition: if a preparation outcome arrived after `RegisterPreparations` but before `WaitForPreparationsActivity` created its bookmark, the stimulus was sent but no bookmark existed → workflow hung forever
- Cleanup after signal was not atomic; crash between signal and cleanup left orphaned tracker docs

**Replacement:** `WaitForPreparationsActivity` now creates **N bookmarks** — one per preparation, each listening for `preparation-completed-{preparationId}`. Each callback increments a counter stored in `WorkflowExecutionContext.Properties` (persisted with the workflow instance). The activity completes itself when the counter reaches N. No external collection; no external fan-in logic.

The `/api/preparation-outcome` endpoint now sends a single per-preparation stimulus (`preparation-completed-{id}`) targeting the fulfilment workflow instance directly by its Elsa instance ID (threaded through via `PrepareCommand.ParentWorkflowInstanceId`).

---

## MEDIUM

### 3. Manual `context.WorkflowExecutionContext.Input` extraction in activities

**Status: PARTIALLY FIXED** (FulfilmentId wired properly in `SendPrepareCommandsActivity` as part of Fix 2; others remain.)

`AllocateFulfilmentActivity`, `SendPrepareCommandsActivity`, and `LogPreparationRequestActivity` dig into the raw input dictionary with `TryGetValue` / `.ToString()` casts. Elsa provides `Input<T>` properties that are bound automatically, type-safe, and visible in the activity signature. Silently returns `"unknown"` on missing keys.

**Remaining:** `AllocateFulfilmentActivity` and `LogPreparationRequestActivity` still use raw input — fixing these is lower priority since they run first in their respective workflows (Input IS available at first execution).

### 4. Race condition in original `WaitForPreparationsActivity`

**Status: FIXED** by Fix 2 redesign. Per-preparation bookmarks created atomically in one `ExecuteAsync` call; each preparation's completion signal targets exactly one bookmark.

### 5. No HTTP retry in `CreateAndSendPreparationOutcomeActivity`

**Status: FIXED.** Added exponential backoff retry (up to 4 attempts: 2s, 4s, 8s). After exhausting retries the activity throws, faulting the preparation workflow — visible in Elsa's `/elsa/api/workflow-instances`. Full dead-letter handling remains open if needed.

### 6. `SendPrepareCommandsActivity` serializes `OrderLines` to a JSON string

**Status: FIXED.** Removed `JsonSerializer.Serialize()` pre-serialization in `CreateFulfilmentHandler` and `SendPrepareCommandsActivity`. Elsa serializes complex types to MongoDB as JSON; when read back they arrive as `JsonElement`, and `.ToString()` on a `JsonElement` array returns the raw JSON — which the receiving activities already pass to `JsonSerializer.Deserialize<T>`. No receiving-side changes needed.

---

## LOW

### 7. `Console.WriteLine` used throughout instead of `ILogger<T>`

**Status: NOT FIXED.** All logging should use `ILogger<T>` for structured logging, configurable verbosity, and trace correlation.

### 8. `InMemoryPreparationCompletionTracker` — dead code

**Status: REMOVED** as part of Fix 2 (deleted with `IPreparationCompletionTracker.cs`).

### 9. No MongoDB indexes defined

**Status: NOT FIXED.** Recommended indexes:
- `elsa_scheduled_tasks`: `{ ResumeAt: 1 }` for polling query
- `fulfilments`: `{ _id: 1, Version: 1 }` for optimistic locking reads

### 10. No input validation in `CreateAllocationHandler`

**Status: NOT FIXED.** Empty `stores` list → divide-by-zero on `i % stores.Count`.

---

## Summary

| # | Finding | Severity | Status |
|---|---------|----------|--------|
| 1 | Custom scheduler (kept — Elsa has no MongoDB scheduler) | Critical | Kept intentionally |
| 2 | Custom tracker (removed, per-preparation bookmarks) | Critical | **Fixed** |
| 3 | Raw input dict extraction in activities | Medium | Partial |
| 4 | Race condition in WaitForPreparationsActivity | Medium | **Fixed** (via #2) |
| 5 | No HTTP retry in preparation outcome | Medium | **Fixed** |
| 6 | OrderLines serialized as JSON string | Medium | **Fixed** |
| 7 | Console.WriteLine instead of ILogger | Low | Open |
| 8 | InMemoryPreparationCompletionTracker dead code | Low | **Fixed** (removed) |
| 9 | No MongoDB indexes | Low | Open |
| 10 | No validation in CreateAllocationHandler | Low | Open |
