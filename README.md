# Elsa Basic Flow

A demonstration of durable fulfilment orchestration using [Elsa Workflows 3.6](https://docs.elsaworkflows.io/) on .NET 10 with MongoDB as the sole persistence store.

The key design goal is **restart safety**: if the API process dies mid-workflow, every workflow resumes from exactly where it left off after restart — including in-flight timers/delays.

---

## Architecture Overview

```
POST /api/fulfilments
        │
        ▼
CreateFulfilmentHandler
  ├── Persist FulfilmentAggregate → MongoDB (fms)
  └── Dispatch FulfilmentWorkflow → Elsa (fms-workflows)
              │
              ├── AllocateFulfilmentActivity
              │     └── POST /api/allocations (retries on 503)
              │
              ├── SendPrepareCommandsActivity
              │     └── Fan-out: one PreparationWorkflow per store allocation
              │
              ├── WaitForPreparationsActivity
              │     └── Suspends with one Event bookmark per preparation
              │
              └── CompleteFulfilmentActivity
                    └── Marks aggregate as Completed
```

### Layer Boundaries

| Project | Responsibility |
|---|---|
| `Elsa.Basic.Flow.Api` | HTTP endpoints + DI wiring only |
| `Elsa.Basic.Flow.Services` | Workflows, activities, command handlers |
| `Elsa.Basic.Flow.Domain` | Aggregates, value objects, interfaces |
| `Elsa.Basic.Flow.Infrastructure` | MongoDB implementations, scheduler, poller |

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)

---

## Getting Started

### 1. Start MongoDB

```powershell
docker compose up -d mongo
```

This starts a MongoDB instance on `localhost:27017` (credentials: `admin` / `admin`).

### 2. Build the solution

```powershell
dotnet build .\Elsa.Basic.Flow.sln
```

### 3. Run the API

```powershell
dotnet run --project .\src\Elsa.Basic.Flow.Api\Elsa.Basic.Flow.Api.csproj
```

The API starts on `http://localhost:5000`. Swagger UI is available at `http://localhost:5000/swagger`.

---

## API Reference

### Create a Fulfilment

```
POST /api/fulfilments
Content-Type: application/json
```

```json
{
  "fulfilmentId": "11111111-0000-0000-0000-000000000001",
  "customerId": "CUST-001",
  "orderId": "ORD-001",
  "lines": [
    { "orderNo": "ORD-001", "sku": "SKU-ALPHA-001", "quantity": 2, "unitOfMeasure": "EA" },
    { "orderNo": "ORD-001", "sku": "SKU-BETA-002",  "quantity": 5, "unitOfMeasure": "EA" }
  ]
}
```

Response: `201 Created` with `{ "id": "<fulfilmentId>" }`

---

### Get a Fulfilment

```
GET /api/fulfilments/{id}
```

Returns the current state of the fulfilment aggregate.

---

### Trigger Allocation (internal / testing)

```
POST /api/allocations
Content-Type: application/json
```

> Note: this endpoint intentionally fails with `503` when `DateTime.UtcNow.Second % 3 == 0` to simulate transient failures. The workflow retries automatically.

---

## Workflow Details

### FulfilmentWorkflow

1. **AllocateFulfilmentActivity** — calls `POST /api/allocations`, retries on `503`
2. **SendPrepareCommandsActivity** — fans out one `PreparationWorkflow` per store allocation
3. **WaitForPreparationsActivity** — suspends with an `Elsa.Event` bookmark per preparation (`preparation-completed-{id}`); resumes when all complete
4. **CompleteFulfilmentActivity** — marks the aggregate `Completed` in MongoDB

### PreparationWorkflow

1. **LogPreparationRequestActivity** — records the prepare command
2. **CreateAndSendPreparationOutcomeActivity** — simulates preparation work and signals the parent workflow via `IStimulusSender` + `EventStimulus`

### Durable Delays

Delays are backed by a custom MongoDB scheduler (`MongoWorkflowScheduler` + `SchedulerPollingService`) so timer state survives process restarts. The `elsa_scheduled_tasks` collection holds pending timers indexed on `ResumeAt`.

---

## Inspecting Workflow State

Use Elsa's built-in management endpoints:

```
GET /elsa/api/workflow-instances
GET /elsa/api/workflow-definitions
```

---

## MongoDB Collections

| Collection | Contents |
|---|---|
| `fms.fulfilments` | Domain aggregates (with optimistic concurrency version) |
| `fms-workflows.*` | Elsa workflow instances, bookmarks, definitions |
| `fms-workflows.elsa_scheduled_tasks` | Durable timer queue |

---

## Configuration

`src/Elsa.Basic.Flow.Api/appsettings.json`:

```json
{
  "MongoDB": {
    "ConnectionString": "mongodb://admin:admin@localhost:27017/?authSource=admin",
    "Database": "fms"
  },
  "ConnectionStrings": {
    "ElsaMongo": "mongodb://admin:admin@localhost:27017/fms-workflows?authSource=admin"
  }
}
```

---

## Key Constraints

- **.NET 10** + **Elsa 3.6.x** — do not downgrade
- **MongoDB only** — no EF Core, no SQL, no in-memory stores
- No MassTransit, MediatR, or AutoMapper
- All workflow state must survive process restarts

---

## Sample Payloads

See [docs/sample-fulfilment-inputs.md](docs/sample-fulfilment-inputs.md) for 2-, 3-, and 4-line order examples ready to paste into Swagger.
