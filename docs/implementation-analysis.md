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

**Status: VERIFIED — KEPT.**

**Verified experimentally (2026-04-26):** `WorkflowExecutionContext.Input` is **not** preserved across process restarts in Elsa 3.6. When a workflow resumes after a crash, `Input` is empty — only `WorkflowExecutionContext.Properties` survive (they are serialised with the workflow instance document in MongoDB).

The activity was temporarily removed and `AllocateFulfilmentActivity` was changed to read from `Input` directly. After a process kill mid-execution, the resumed workflow sent an allocation request with empty order lines, which faulted the workflow after all 4 retry attempts.

`InitialiseFulfilmentPropertiesActivity` is necessary. Its doc comment has been updated to state this explicitly so future maintainers do not remove it again.

Also confirmed: Elsa has a built-in "Restarting interrupted workflows" mechanism that detects workflow instances that were mid-execution at the time of a crash and re-dispatches them on startup. This fires approximately 5 minutes after startup (coincides with the `SchedulerPollingService` max sleep interval).

---

### 20. Two MongoDB databases — no transactional boundary between domain and Elsa state

**Status: FIXED.**

`ElsaMongo` connection string updated to use `fms` (same database as the domain). Elsa's collections (`workflow_instances`, `workflow_definitions`, `elsa_scheduled_tasks`, etc.) now coexist with domain collections (`fulfilments`) in a single database, enabling MongoDB multi-document transactions across the boundary if needed.

`CreateFulfilmentHandler` writes the domain aggregate to `fms.fulfilments`, then creates the Elsa workflow instance in `fms-workflows`. These are separate MongoDB databases; MongoDB multi-document transactions do not span databases.

If the workflow creation step fails after the aggregate is persisted, the aggregate is orphaned (exists in `fms` with no corresponding workflow). There is no compensating action.

**Recommendation:** Either:

- Consolidate Elsa and domain state into a single MongoDB database (simplest — no code change beyond connection strings, since both use the same MongoDB instance).
- Or accept the orphan risk and add a reconciliation job that detects aggregates with no corresponding workflow instance.

A single database also enables MongoDB multi-document transactions for true atomicity across aggregate creation and workflow dispatch.

---

## Fourth-pass findings (2026-04-26)

### 21. `WaitForPreparationsActivity` — counter increment after failable POST

**Status: FIXED.**

`OnPreparationCompletedAsync` called `EnsureSuccessStatusCode()` on the container POST before calling `IncrementPrepCompletedAsync`. Because `AutoBurn = true`, the bookmark is consumed on entry to the callback. If the POST threw, the `$inc` was never reached, `completed` never reached `total`, and `CompleteActivityAsync` was never called — the workflow hung forever.

**Fix:** Moved `IncrementPrepCompletedAsync` to execute **before** the container POST. Wrapped the POST in `try/catch` so failures are logged as warnings but do not prevent the counter from being incremented or the activity from completing. The container POST is best-effort enrichment; the fan-in gate must not depend on it.

---

### 22. `FulfilmentStatus` never transitions — always shows `Created`

**Status: FIXED.**

`FulfilmentStatus` had only one value (`Created`). `CompleteFulfilmentActivity` only logged and completed; it never updated the aggregate.

**Fix:**
- Added `Completed` to `FulfilmentStatus`.
- Added `Complete()` to `FulfilmentAggregate` — sets `Status = FulfilmentStatus.Completed` and increments `Version`.
- Rewrote `CompleteFulfilmentActivity` to load the aggregate via `IFulfilmentRepository`, call `Complete()`, save it, then complete the activity.

`GET /api/fulfilments/{id}` now returns `"Status": "Completed"` after the workflow finishes.

---

### 23. `SendPrepareCommandsActivity` — no retry on preparation POST

**Status: FIXED.**

A failed `POST /api/preparations` immediately threw via `EnsureSuccessStatusCode()`, which faulted the fulfilment workflow. The preparation workflow for that store never started, so its fan-in bookmark was never registered, and `WaitForPreparationsActivity` would wait forever for a signal that could never arrive.

**Fix:** Added exponential-backoff retry (attempts at 0 s, then 2 s, 4 s, 8 s — 4 attempts total) around each `PostAsJsonAsync` call. After exhausting retries the activity throws `InvalidOperationException`, which faults the fulfilment workflow immediately with a clear message rather than leaving it in a silent deadlock.

> Note: Unlike `AllocateFulfilmentActivity` (which uses the durable `While`+`Delay` pattern), the retry here is in-process and non-durable across restarts. For a more robust solution, the same bookmark+scheduler pattern from #16/#17 could be applied, but the inline retry is a meaningful improvement over no retry.

---

## Fifth-pass findings (2026-04-26)

### 24. `LogPreparationRequestActivity` — Input lost on restart (same bug as #19)

**Status: FIXED.**

`LogPreparationRequestActivity` is the first activity in `PreparationWorkflow`. It reads `PrepareCommandId`, `StoreId`, and `Lines` from `WorkflowExecutionContext.Input` and outputs them as workflow variables. If the process crashes during this activity, Elsa re-runs it on restart with empty `Input` (verified — see #19). The outputs would be `"(unknown)"` and `"[]"`, causing `CreateAndSendPreparationOutcomeActivity` to build 0 containers and send an outcome with an unknown GUID, silently corrupting the fan-in counter and the aggregate.

**Fix:** Applied the same Properties-copy pattern as `InitialiseFulfilmentPropertiesActivity`. `LogPreparationRequestActivity` now copies `PrepareCommandId`, `StoreId`, and `Lines` into `WorkflowExecutionContext.Properties` (under keys `_prep_command_id`, `_prep_store_id`, `_prep_lines_json`) before setting its outputs. Properties survive restarts; the workflow variables set as outputs are committed after this activity completes and are available to all downstream activities.

---

### 25. `SchedulerPollingService` — obsolete `ExportWorkflowStateAsync` API

**Status: FIXED.**

`SchedulerPollingService` called `IWorkflowRuntime.ExportWorkflowStateAsync(instanceId, ct)`, which is marked `[Obsolete]` in Elsa 3.6. Replaced with the client API: `runtime.CreateClientAsync(instanceId, ct)` returns an instance-scoped client; `client.ExportStateAsync(ct)` performs the equivalent existence check and still throws `WorkflowInstanceNotFoundException` for stale tasks.

---

### 26. `PreparationWorkflow` uses `WriteLine` activities — bypasses formatter

**Status: FIXED.**

`new WriteLine("[PreparationWorkflow] Starting delay...")` and `new WriteLine("...Delay completed...")` wrote directly to `Console.Out`, bypassing `WorkflowConsoleFormatter`. Removed both from the workflow. "Delay completed" is now logged via `ILogger` at the start of `CreateAndSendPreparationOutcomeActivity.ExecuteAsync`; the delay info is already present in `LogPreparationRequestActivity`'s existing log line.

---

### 27. Retry loop in `SendPrepareCommandsActivity` catches `OperationCanceledException`

**Status: FIXED.**

`catch (Exception ex) when (attempt < delays.Length)` was catching all exceptions including `OperationCanceledException`. Added `&& ex is not OperationCanceledException` to the when-guard so cancellation propagates immediately rather than triggering a retry.

---

### 28. `SendPrepareCommandsActivity` — `if (!sent) throw` was dead code

**Status: FIXED.**

The `var sent = false` flag and `if (!sent) throw new InvalidOperationException(...)` below the retry loop were never reached. When the last attempt fails, the `when (attempt < delays.Length)` guard is false so the exception propagates out of the method before the check. Removed the flag and the dead throw; last-attempt exceptions now propagate naturally.

---

### 29. `ElsaPreparationOutcomeHandler` — hardcoded `"Elsa.Event"` string literal

**Status: FIXED.**

`stimulusSender.SendAsync("Elsa.Event", ...)` used a raw string literal. `WaitForPreparationsActivity` uses `RuntimeStimulusNames.Event` for the matching bookmark name. Replaced the literal with `RuntimeStimulusNames.Event` so both sides track the same constant.

---

### 30. Emoji in log line

**Status: FIXED.**

`CreateAndSendPreparationOutcomeActivity` logged container details with a `📦` emoji. Removed per project rules (no emojis unless explicitly requested).

---

### 31. `AllocateFulfilmentActivity` — dictionary indexer instead of `TryGetValue`

**Status: FIXED.**

`props[key]?.ToString()` throws `KeyNotFoundException` if a key is absent. Replaced all four Properties reads with `TryGetValue` with safe fallbacks, consistent with the rest of the codebase.

---

## Sixth-pass findings (2026-04-26)

### 32. `BuildContainers` — `OrderLineNo` set to `line.Sku` (copy-paste bug)

**Status: OPEN.**

In `CreateAndSendPreparationOutcomeActivity.BuildContainers`:

```csharp
buckets[i % containerCount].Add(new AllocatedLine(line.Sku, line.Sku, line.Quantity));
//                                                  ^^^^^^^^ should be line.OrderNo
```

`AllocatedLine` is `record AllocatedLine(string OrderLineNo, string ArticleId, int Quantity)`. The first argument (`OrderLineNo`) is populated with `line.Sku` instead of `line.OrderNo`. Every `AllocatedLine` written to MongoDB has `OrderLineNo == ArticleId == Sku` — the order line reference is silently lost.

**Fix:** change to `new AllocatedLine(line.OrderNo, line.Sku, line.Quantity)`.

---

### 33. `TrySendOutcomeAsync` swallows `OperationCanceledException`

**Status: OPEN.**

```csharp
catch (Exception ex)   // catches OperationCanceledException
{
    logger.LogWarning(...);
    succeeded = false;
}
```

When the app shuts down, `context.CancellationToken` is cancelled and `PostAsJsonAsync` throws `OperationCanceledException`. This is caught, `succeeded = false`, and the code schedules a durable retry in MongoDB instead of propagating the cancellation. The same bug was fixed in `SendPrepareCommandsActivity` (see #27) but this activity was missed.

**Fix:** add `when (ex is not OperationCanceledException)` to the catch guard.

---

### 34. `ElsaPreparationOutcomeHandler` — `StimulusMetadata.WorkflowInstanceId` never set; `PrepareCommand` lacks `ParentWorkflowInstanceId`

**Status: OPEN.**

```csharp
await stimulusSender.SendAsync(
    RuntimeStimulusNames.Event,
    new EventStimulus($"preparation-completed-{payload.Id}"),
    new StimulusMetadata { Input = ... },   // no WorkflowInstanceId
    ct);
```

`AGENTS.md` states the intended pattern is `StimulusMetadata { WorkflowInstanceId = ... }` threaded through via `PrepareCommand.ParentWorkflowInstanceId`. Neither `PrepareCommand` carries that field, nor does `SendPrepareCommandsActivity` thread through the parent instance ID. As a result Elsa performs a global bookmark scan across all live workflow instances rather than targeting one. Functionally safe because the event name embeds a UUID, but contradicts the design intent and scales poorly under many concurrent workflows.

**Fix:** add `ParentWorkflowInstanceId` to `PrepareCommand` and the anonymous request object in `SendPrepareCommandsActivity`; surface it in `PreparationOutcomePayload`; set `StimulusMetadata.WorkflowInstanceId` in `ElsaPreparationOutcomeHandler`.

---

### 35. `AllocateFulfilmentActivity` — null `AllocationResult` from `ReadFromJsonAsync` not guarded

**Status: OPEN.**

```csharp
var result = await response.Content.ReadFromJsonAsync<AllocationResult>(context.CancellationToken);
context.Set(Result, result);   // result may be null
```

If the API returns 200 with an empty or malformed body, `result` is null. `SendPrepareCommandsActivity` then calls `context.Get(AllocationResult)!` which throws `NullReferenceException`, faulting the workflow with a confusing message.

**Fix:** add a null-check after `ReadFromJsonAsync` and throw a clear `InvalidOperationException` if null.

---

### 36. `Guid.Parse(request.FulfilmentId)` in endpoint — `FormatException` returns 500 instead of 400

**Status: OPEN.**

```csharp
var fulfilmentId = Guid.Parse(request.FulfilmentId);   // throws FormatException on bad input
```

An invalid GUID in the request body propagates unhandled as a 500 response. Should be validated at the API boundary.

**Fix:** use `Guid.TryParse` and return `Results.BadRequest(...)` on failure.

---

### 37. `WaitForPreparationsActivity` — null-bang `!` on `PrepareCommands` input

**Status: OPEN.**

```csharp
var commands = context.Get(PrepareCommands)!;
```

If `PrepareCommands` is unset, this throws `NullReferenceException` with no diagnostic message. Inconsistent with every other activity in the codebase which uses `TryGetValue`/null checks or explicit guard clauses.

**Fix:** replace with a guarded check and a clear `InvalidOperationException`.

---

### 38. `WaitForPreparationsActivity` — zero-command case is a silent deadlock

**Status: OPEN.**

If `commands.Count == 0`, `ExecuteAsync` registers no bookmarks and returns without calling `CompleteActivityAsync`. The workflow suspends at this activity forever. The guard in `CreateAllocationHandler` (#10) prevents this in the happy path, but there is no in-activity defensive short-circuit.

**Fix:** add `if (commands.Count == 0) { await context.CompleteActivityAsync(); return; }` at the start of `ExecuteAsync`.

---

### 39. Analysis doc — stale paragraph under #20 contradicts its own "Fixed" status

**Status: OPEN (documentation).**

The paragraph immediately below the "Fixed" status for #20 still reads:

> `CreateFulfilmentHandler` writes the domain aggregate to `fms.fulfilments`, then creates the Elsa workflow instance in `fms-workflows`. These are separate MongoDB databases…

`fms-workflows` no longer exists — both now use `fms`. The concern paragraph should be removed or struck through so it does not mislead future maintainers.

---

## Seventh-pass findings (2026-04-28 — post-subworkflow redesign)

The previous passes analysed a flat fulfilment workflow with `SendPrepareCommandsActivity` + `WaitForPreparationsActivity`. The architecture was replaced with a parent/child subworkflow fan-out model (`DispatchAndWaitPreparationsActivity` + `StorePreparationSubWorkflow`). Several earlier open findings became obsolete; new findings emerged from the new design.

---

### 40. `DispatchAndWaitPreparationsActivity` — `ExecuteAsync` not idempotent; crash mid-loop spawns duplicate subworkflows

**Status: Open — critical.**

`ExecuteAsync` generates a fresh `Guid.NewGuid()` per store inside the dispatch loop, creates a subworkflow instance, and creates two bookmarks. With `ExecutingActivityStrategy`, Elsa commits the workflow state as "this activity is executing" **before** `ExecuteAsync` runs. If the process crashes partway through the loop (say, after dispatching stores 1–2 out of 5), on restart Elsa re-runs `ExecuteAsync` from scratch:

- New GUIDs are generated for **all** stores including the two already dispatched.
- All 5 subworkflows are re-dispatched — stores 1 and 2 each now have two running subworkflow instances with different `prepCommandId`s.
- The old subworkflows for stores 1 and 2 complete and send `store-prep-completed-{old-id}` signals, but the parent has no bookmarks for those old IDs. The signals are discarded; the counter is not incremented.
- The new batch of 5 all complete and the parent finishes correctly.
- **Net effect:** the allocation service and preparation service each receive duplicate commands for stores 1 and 2. This is silent over-processing, not a hang — but it violates at-most-once delivery to downstream systems.

**Fix:** Generate all `prepCommandId`s upfront, store them (alongside their store IDs) in `WorkflowExecutionContext.Properties` before the dispatch loop begins. On each `ExecuteAsync` invocation, check Properties first — if the IDs are already there, use them; only dispatch subworkflows that don't already have a matching Elsa workflow instance (checked via `CorrelationId`). This makes `ExecuteAsync` idempotent.

---

### 41. `WaitForStorePreparationOutcomeActivity` — callback not crash-safe after parent signal is sent

**Status: Open — medium.**

`OnOutcomeReceivedAsync` (1) applies containers via HTTP, (2) sends `store-prep-completed-{prepCommandId}` to the parent, (3) calls `CompleteActivityAsync`. With `AutoBurn = true`, the bookmark is consumed the moment the callback is entered. If the process crashes between steps (2) and (3):

- On restart, `ExecuteAsync` runs again and creates a new bookmark for `preparation-completed-{prepCommandId}`.
- But `ElsaPreparationOutcomeHandler` has already processed that event — it will not send the stimulus again.
- The new bookmark never fires → the subworkflow is permanently suspended.
- Meanwhile the parent's counter was already incremented in step (2), so the parent may complete while this subworkflow is zombied in MongoDB.

**Fix:** Before sending the parent signal in the callback, write a "callback-completed" flag to `WorkflowExecutionContext.Properties`. At the start of `ExecuteAsync`, check for that flag — if set, the callback already ran, so skip bookmark creation and call `CompleteActivityAsync` immediately. This makes the activity restartable without re-registering a dead bookmark.

---

### 42. `OnStorePrepFailedAsync` — sibling bookmarks orphaned on child failure

**Status: Open — medium.**

When one child's fault signal fires, `OnStorePrepFailedAsync` throws, faulting the `FulfilmentWorkflow`. The remaining success/failure bookmarks for all other in-flight children remain registered in Elsa's bookmark store against a now-faulted workflow instance.

As those children eventually complete, they send signals that Elsa tries to match. Elsa will find the bookmarks and attempt to resume a faulted workflow — behaviour depends on Elsa's internal handling of bookmark delivery to faulted instances, but at minimum those bookmarks are never garbage-collected by application code and accumulate in `workflow_bookmarks`.

**Fix:** Before throwing in `OnStorePrepFailedAsync`, explicitly cancel or remove all remaining sibling bookmarks. Elsa does not expose a direct "delete all bookmarks for this activity instance" API, but iterating `context.WorkflowExecutionContext.Bookmarks` and removing those matching the activity's pattern is possible. Alternatively, accept the accumulation as a low-frequency operational concern and add a MongoDB TTL index on the `workflow_bookmarks` collection.

---

### 43. `ElsaPreparationOutcomeHandler` — `WorkflowInstanceId` still missing from `StimulusMetadata`

**Status: Open — medium (updated from #34).**

Finding #34 flagged this in the previous architecture where `PrepareCommand` was supposed to carry `ParentWorkflowInstanceId`. That class no longer exists. The issue persists in the new design: `ElsaPreparationOutcomeHandler` sends the `preparation-completed-{prepCommandId}` stimulus without a `WorkflowInstanceId`, forcing Elsa to scan all bookmarks globally.

In the current architecture, the correct target is the `StorePreparationSubWorkflow` instance. The subworkflow's Elsa instance ID is known at dispatch time (returned by `client.WorkflowInstanceId` in `DispatchAndWaitPreparationsActivity`). Fixing this requires threading that instance ID to the preparation service (in the preparation command POST body) and returning it in `PreparationOutcomePayload`, so `ElsaPreparationOutcomeHandler` can set `StimulusMetadata.WorkflowInstanceId`.

Without this fix, correctness is maintained (the `prepCommandId` UUID in the event name is unique enough to avoid cross-matching), but at scale with thousands of concurrent workflows the global scan degrades performance and adds lock contention on the bookmark store.

---

### 44. `SendStorePreparationCommandActivity` — duplicates `DurableRetryActivity` boilerplate

**Status: Open — low.**

`SendStorePreparationCommandActivity` implements its own durable retry loop (count key in Properties, exponential backoff, bookmark + `IWorkflowScheduler`) rather than extending `DurableRetryActivity`. The reason is that `DurableRetryActivity` throws on exhaustion, whereas this activity must set `Succeeded = false` and call `CompleteActivityAsync` so the subworkflow can branch via `If/Else` rather than fault.

The divergence is justified but the ~40 lines of boilerplate are identical. `DurableRetryActivity` could gain a `virtual OnExhaustedAsync(ActivityExecutionContext context)` method — the default implementation throws; subclasses can override to set an output and complete cleanly. This would reduce `SendStorePreparationCommandActivity` to just the HTTP call and the exhaustion override.

---

### 45. `DispatchAndWaitPreparationsActivity` — null-bang on `AllocationResult` input

**Status: Open — low.**

```csharp
var result = context.Get(AllocationResult)!;
```

If `AllocationResult` is not wired correctly at the workflow level, this throws `NullReferenceException` with no diagnostic message. Same class of issue as finding #37 (now closed), now present in the replacement activity. Replace with a guarded check and a clear `InvalidOperationException`.

---

### 46. `OnOutcomeReceivedAsync` — `OperationCanceledException` swallowed in container POST

**Status: Open — low.**

```csharp
catch (Exception ex)
{
    logger.LogWarning(ex, "...failed to apply containers...");
}
```

The broad catch in the container application block captures `OperationCanceledException`. On graceful shutdown, the activity logs a warning and continues instead of propagating cancellation. This is the same class of bug as fixed in #27 and still open in #33.

**Fix:** Add `when (ex is not OperationCanceledException)` to the catch guard.

---

### 47. Stale open findings closed by the subworkflow redesign

**Status: Documentation.**

The following findings from earlier passes reference classes that no longer exist:

- **#37** (`WaitForPreparationsActivity` — null-bang on `PrepareCommands`): `WaitForPreparationsActivity` was deleted. `DispatchAndWaitPreparationsActivity` has the analogous issue documented as #45 above.
- **#38** (`WaitForPreparationsActivity` — zero-command deadlock): Replaced activity correctly guards the zero-allocation case on lines 31–36 of `DispatchAndWaitPreparationsActivity`. **This finding is now closed.**
- **#34** (`PrepareCommand.ParentWorkflowInstanceId`): `PrepareCommand` no longer exists. The underlying concern is carried forward in #43 above.
- **#18** (`WaitForPreparationsActivity` couples workflow to domain repository): `WaitForPreparationsActivity` no longer exists. The same coupling now appears in `DispatchAndWaitPreparationsActivity.OnStorePrepSignalReceivedAsync` → `IFulfilmentRepository.IncrementPrepCompletedAsync`. The architectural concern from #18 still applies.

---

### 48. `WorkflowDefinitionHandle.ByDefinitionId` resolves to latest — subworkflow versioning risk

**Status: Open — low.**

```csharp
WorkflowDefinitionHandle.ByDefinitionId(nameof(StorePreparationSubWorkflow))
```

`ByDefinitionId` resolves to the latest published version at dispatch time. If `StorePreparationSubWorkflow` is updated and republished mid-deployment while a `FulfilmentWorkflow` is running, sibling children spawned before and after the deployment boundary run different versions of the subworkflow. This is typically harmless in practice but worth knowing — use `ByDefinitionVersionId` and pin the version in configuration if sibling-version consistency is required.

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
| 18 | DispatchAndWaitPreparationsActivity couples workflow to domain repository (WaitForPreparationsActivity renamed) | Medium | **Open** |
| 19 | InitialiseFulfilmentPropertiesActivity — purpose unclear / may be unnecessary | Low | Verified necessary — Input lost on restart |
| 20 | Two MongoDB databases — no transactional boundary | Low | **Fixed** — Elsa now uses `fms` database |
| 21 | WaitForPreparationsActivity — $inc after failable POST → silent deadlock | Critical | **Fixed** |
| 22 | FulfilmentStatus never transitions beyond Created | High | **Fixed** |
| 23 | SendPrepareCommandsActivity — no retry on preparation POST | High | **Fixed** |
| 24 | PreparationWorkflow — Input lost on restart (LogPreparationRequestActivity) | High | **Fixed** |
| 25 | ExportWorkflowStateAsync obsolete API (CS0618 warning) | Medium | **Fixed** |
| 26 | PreparationWorkflow uses WriteLine — bypasses formatter | Medium | **Fixed** |
| 27 | Retry loop catches OperationCanceledException | Medium | **Fixed** |
| 28 | `if (!sent) throw` dead code in SendPrepareCommandsActivity | Low | **Fixed** |
| 29 | `"Elsa.Event"` string literal instead of RuntimeStimulusNames.Event | Low | **Fixed** |
| 30 | Emoji in log line | Low | **Fixed** |
| 31 | Dictionary indexer instead of TryGetValue in AllocateFulfilmentActivity | Low | **Fixed** |
| 32 | `BuildContainers` — `OrderLineNo` set to `line.Sku` (copy-paste bug, silent data corruption) | Medium | **Open** |
| 33 | `TrySendOutcomeAsync` swallows `OperationCanceledException` on shutdown | Medium | **Open** |
| 34 | `ElsaPreparationOutcomeHandler` — `WorkflowInstanceId` not set; `PrepareCommand` lacks `ParentWorkflowInstanceId` | Medium | Superseded by #43 |
| 35 | `AllocateFulfilmentActivity` — null `AllocationResult` from `ReadFromJsonAsync` not guarded | Low | **Open** |
| 36 | `Guid.Parse` in `/api/fulfilments` endpoint — `FormatException` returns 500 instead of 400 | Low | **Open** |
| 37 | `WaitForPreparationsActivity` — null-bang `!` on `PrepareCommands` input | Low | Closed — activity deleted; see #45 |
| 38 | `WaitForPreparationsActivity` — zero-command case is a silent deadlock | Low | **Fixed** — replacement activity guards correctly |
| 39 | Analysis doc — stale `fms-workflows` paragraph under #20 contradicts Fixed status | Docs | **Open** |
| 40 | `DispatchAndWaitPreparationsActivity` — `ExecuteAsync` not idempotent; crash mid-loop spawns duplicate subworkflows | Critical | **Fixed** — plan split into `InitialisePreparationPlanActivity` |
| 41 | `WaitForStorePreparationOutcomeActivity` — callback not crash-safe after parent signal is sent | Medium | **Open** |
| 42 | `OnStorePrepFailedAsync` — sibling bookmarks orphaned on child failure | Medium | **Open** |
| 43 | `ElsaPreparationOutcomeHandler` — `WorkflowInstanceId` still missing from `StimulusMetadata` | Medium | **Open** |
| 44 | `SendStorePreparationCommandActivity` — duplicates `DurableRetryActivity` boilerplate | Low | **Open** |
| 45 | `DispatchAndWaitPreparationsActivity` — null-bang on `AllocationResult` input | Low | **Open** |
| 46 | `OnOutcomeReceivedAsync` — `OperationCanceledException` swallowed in container POST | Low | **Open** |
| 47 | Stale findings #34, #37, #38 closed or superseded by subworkflow redesign | Docs | **Open** |
| 48 | `WorkflowDefinitionHandle.ByDefinitionId` resolves to latest — subworkflow versioning risk | Low | **Open** |
