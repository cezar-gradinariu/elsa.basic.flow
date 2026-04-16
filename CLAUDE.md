# Project Rules for Claude

## Before Any Implementation

- **Always read the Elsa docs at https://docs.elsaworkflows.io/ before implementing anything Elsa-related.**
- Check what Elsa already provides before writing custom code — activities, services, extension points, etc.
- Adhere to Elsa's documented best patterns. Do not reinvent wheel.

## Technology Constraints

- **Elsa:** 3.6 or latest stable. No older versions.
- **MongoDB:** Latest stable driver. MongoDB is the persistence store for everything — workflows, activities, bookmarks, scheduling. All flows and activities must be persisted to MongoDB so workflows survive process restarts.
- **.NET:** .NET 10, latest C# language version.
- **No Entity Framework** — not allowed anywhere.
- **No MassTransit** — not allowed anywhere.
- **No MediatR** — not allowed anywhere.
- **No AutoMapper** — not allowed anywhere.

## NuGet & Libraries

- Use what Elsa's ecosystem offers — check Elsa-related NuGet packages (`Elsa.*`, `Elsa.Workflows.*`, etc.) before building custom solutions.
- **Always ask the user before adding any new NuGet package or library.**

## Workflow Persistence & Durability

- All workflows and activities must be persisted to MongoDB so that if the process dies, the workflow resumes from where it left off — this is a primary design goal.
- Use Elsa's built-in persistence and scheduling infrastructure. Custom schedulers/pollers must also use MongoDB.
- Never use in-memory-only state for anything that must survive a restart.

## Debugging & Validation

- Use Elsa's built-in workflow endpoints (e.g., `/elsa/api/workflow-instances`, `/elsa/api/workflow-definitions`) to inspect and validate workflow state during debugging.
- Do not build custom admin/debug endpoints that duplicate what Elsa already exposes.

## Architecture & Layer Separation

- **Domain project** contains only DDD constructs: aggregates, entities, value objects, domain events, and domain service interfaces. No workflow code, no repository implementations, no infrastructure concerns.
- **Domain** may expose interfaces (e.g., `IFulfilmentRepository`) but never implements them — implementations belong in Infrastructure.
- **Api project** contains no business logic. Endpoints parse HTTP input and immediately delegate to a handler in the Services layer. No workflow creation, no domain manipulation directly in `Program.cs` beyond wiring.
- **Services project** is where all workflows, activities, and application handlers live. This is the orchestration layer.
- **Infrastructure project** implements interfaces defined in Domain and Services: repositories, schedulers, trackers, external HTTP clients, etc.

## File & Folder Organisation

- Group files by **feature/functionality first** (e.g., `Fulfilment/`, `Preparation/`, `Allocation/`), not by type.
- Within each feature folder, organise by kind in this order:
  - `Commands/` — command models and handlers
  - `Workflows/` — workflow definitions
  - `Activities/` — individual activity classes
- Keep a consistent structure across all projects so the same feature is easy to find regardless of which layer you are in.

## General Principles

- Prefer Elsa's built-in activities, bookmarks, and stimuli over custom implementations.
- Ask the user before introducing any external dependency or architectural pattern not already present in the project.
