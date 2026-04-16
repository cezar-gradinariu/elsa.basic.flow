# Elsa 3.6 Embedded — Key Learnings

## Event activity internals

The `Event` activity does NOT use `EventTokenPayload` for its bookmark.
It uses `EventStimulus` from `Elsa.Workflows.Runtime.Stimuli`:

```csharp
new EventStimulus(eventName)  // correct
new EventTokenPayload(...)    // wrong — different type, hash won't match
```

The bookmark name stored in `IBookmarkStore` is `"Elsa.Event"` (not the event name).
The hash differentiates events — computed from the activity type name + `EventStimulus`.

## Resuming a suspended Event activity

Use `IStimulusSender`, not `IEventPublisher`:

```csharp
await stimulusSender.SendAsync(
    "Elsa.Event",
    new EventStimulus(eventName),
    new StimulusMetadata { WorkflowInstanceId = instanceId },
    ct);
```

`IEventPublisher.PublishAsync` with `isLocalEvent = true` routes through an
in-process notification channel — it cannot reach a suspended workflow.
`isLocalEvent = false` targets a distributed dispatcher that is not wired up
in a minimal embedded setup. Either way it doesn't work here.

## Bookmark storage

`IWorkflowRuntime.CreateClientAsync()` + `client.CreateAndRunInstanceAsync()`
stores bookmarks in `IBookmarkStore` (in-memory by default).
The raw `IWorkflowRunner.RunAsync<T>()` does NOT store to `IBookmarkStore` —
bookmarks only live in `RunWorkflowResult.WorkflowState.Bookmarks`.

Use the raw runner when you own the resume loop (e.g. the Greeting scenario).
Use `IWorkflowRuntime` + `IStimulusSender` when signals come from outside
(e.g. the Approval scenario).

## Distributed setup (notes, not implemented)

For multiple instances of this app:
- Add MongoDB nuget for shared `IBookmarkStore` + `IWorkflowInstanceStore`
- Signals still delivered via `IStimulusSender` — any node can resume any workflow
- For signal delivery across nodes: HTTP endpoint or a queue consumer
  (`BackgroundService` consuming from Azure Service Bus → `IStimulusSender`)
- MassTransit not required unless you want transport portability or
  Elsa's own MassTransit integration for workflow coordination

For generic Service Bus messaging (nothing to do with workflow coordination):
- Use `Azure.Messaging.ServiceBus` SDK directly
- Inject `ServiceBusClient` as a normal DI service
- Call from a custom activity or any other service — Elsa is not involved

## Scenario pattern

Each scenario owns its workflow(s), activities, and run logic.
`Program.cs` only holds the menu and host setup — it never changes for new scenarios.

To add a scenario:
1. Create `Scenarios/YourScenario/` folder
2. Implement `IWorkflowScenario` (Register + RunAsync)
3. Add one entry to the array in `Program.cs`
