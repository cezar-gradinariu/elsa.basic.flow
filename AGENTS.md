# AGENTS.md

## Scope
- This solution demonstrates durable Elsa fulfilment orchestration on MongoDB; prioritize restart-safe workflow behavior.

## Non-negotiable constraints
- Keep `.NET 10` + Elsa `3.6.*` usage consistent with `src/Elsa.Basic.Flow.Api/Elsa.Basic.Flow.Api.csproj` and `src/Elsa.Basic.Flow.Services/Elsa.Basic.Flow.Services.csproj`.
- Keep MongoDB as persistence store for domain + workflow runtime/management (`src/Elsa.Basic.Flow.Api/Program.cs`, `docker-compose.yml`).
- Do not introduce EF, MassTransit, MediatR, or AutoMapper; ask before adding any NuGet package (`CLAUDE.md`).

## Layer boundaries (follow existing split)
- `src/Elsa.Basic.Flow.Api`: endpoint parsing + DI wiring only; delegate immediately to service handlers.
- `src/Elsa.Basic.Flow.Services`: workflows, activities, command handlers (`Fulfilment`, `Preparation`, `Allocation`).
- `src/Elsa.Basic.Flow.Domain`: aggregate/value objects/interfaces only (no workflow or infra implementation).
- `src/Elsa.Basic.Flow.Infrastructure`: Mongo implementations (`MongoFulfilmentRepository`, `MongoWorkflowScheduler`, poller).

## Fulfilment data flow (critical)
- `POST /api/fulfilments` persists aggregate then creates/dispatches `FulfilmentWorkflow` (`CreateFulfilmentHandler`).
- `FulfilmentWorkflow`: `AllocateFulfilmentActivity` -> `SendPrepareCommandsActivity` -> `WaitForPreparationsActivity` -> `CompleteFulfilmentActivity`.
- `SendPrepareCommandsActivity` fans out one `PreparationWorkflow` per store allocation and dispatches each via `IWorkflowDispatcher`.
- `WaitForPreparationsActivity` performs fan-in with one `Elsa.Event` bookmark per preparation (`preparation-completed-{id}`), tracking counts in persisted workflow properties.
- `POST /api/preparation-outcome` updates aggregate with optimistic concurrency retry, then resumes parent workflow via `IStimulusSender` + `EventStimulus`.

## Durability patterns to preserve
- Elsa Mongo registration order in `src/Elsa.Basic.Flow.Api/Program.cs` matters: persistence before runtime features.
- Delay durability is custom and required: `src/Elsa.Basic.Flow.Infrastructure/MongoWorkflowScheduler.cs` + `src/Elsa.Basic.Flow.Infrastructure/SchedulerPollingService.cs` + `src/Elsa.Basic.Flow.Infrastructure/SchedulerWakeSignal.cs`.
- Keep startup index creation for `elsa_scheduled_tasks.ResumeAt` in `src/Elsa.Basic.Flow.Api/Program.cs`.
- Preserve aggregate version checks in `src/Elsa.Basic.Flow.Infrastructure/MongoFulfilmentRepository.cs` (`ReplaceOne` filter on id + expected version).

## Codebase-specific conventions
- Input keys are string-based and shared across activities (`FulfilmentId`, `OrderNo`, `StoreId`, `OrderLines`, `ParentWorkflowInstanceId`); renaming breaks workflow contracts.
- `OrderLines` are intentionally JSON-serialized when creating workflow instances (`CreateFulfilmentHandler`, `SendPrepareCommandsActivity`) to avoid resume/deserialization issues.
- For suspended event resumes, use `IStimulusSender.SendAsync("Elsa.Event", new EventStimulus(...), new StimulusMetadata { WorkflowInstanceId = ... })`.

## Local workflows (verified where noted)
- Start Mongo: `Set-Location "C:\Users\1230075\wxRepos\Elsa.Basic.Flow"; docker compose up -d mongo`.
- Build solution (verified 2026-04-20): `Set-Location "C:\Users\1230075\wxRepos\Elsa.Basic.Flow"; dotnet build .\Elsa.Basic.Flow.sln`.
- Run API: `Set-Location "C:\Users\1230075\wxRepos\Elsa.Basic.Flow"; dotnet run --project .\src\Elsa.Basic.Flow.Api\Elsa.Basic.Flow.Api.csproj`.
- Use existing Elsa endpoints for debugging state: `/elsa/api/workflow-instances`, `/elsa/api/workflow-definitions`.

## AI-instruction sources discovered
- Requested glob search matched only `CLAUDE.md` in this repository.

