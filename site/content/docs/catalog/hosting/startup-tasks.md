# One-Shot Startup Tasks

## Header

- **ID**: `hosting.startup-tasks`
- **Name**: One-shot startup tasks
- **Maturity**: GA
- **Summary**: Manifest contract for startup work that must complete before any background loop starts.

## What It Does

`IStartupTask` defines one method, `RunAsync(CancellationToken)`. Modules register startup task types during composition. The host orchestrator runs each task in order during `StartAsync`, then starts background loops only if all startup tasks succeed.

Common uses include schema ensure and validation for PostgreSQL-backed inbox and outbox storage.

## Public Surface

### Consumer Contracts

- `IStartupTask.RunAsync(CancellationToken)`
- `IModuleConfiguration.RegisterStartupTask(Type)`
- `IModuleConfiguration.StartupTasks`

### Registration

- Startup tasks are manifest registrations, not direct `IHostedService` registrations.
- Examples: `PostgreSqlInboxSchemaInitializer`, `PostgreSqlOutboxSchemaInitializer`.

## Packages

- `LiteBus.Runtime.Abstractions`
- `LiteBus.Runtime`
- Feature packages that contribute startup tasks (for example PostgreSQL storage modules)

## Requires

- `hosting.generic-host-orchestrator`

## Invariants

- Startup tasks execute sequentially in registration order.
- Startup failure prevents background loops from starting.
- Startup task types are deduplicated by first registration.

## Non-Goals

- Long-running loops.
- Generic retry policy for startup failures.

## Observability

- Failures propagate from host startup and fail closed.
- No dedicated startup-task meter is emitted by hosting core.

## Test Coverage

### Covered Use Cases

#### `ModuleConfigurationTests.RegisterStartupTask_ShouldPreserveFirstRegistrationOrderAndDeduplicate`

- **Use case**: duplicate startup task registration deduplication
- **Test kind**: Unit
- **Description**: registers same startup type twice
- **Behavior**: inspects startup task manifest list
- **Expected outcome**: one entry in first registration order
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/ModuleConfigurationTests.cs`

#### `MicrosoftBackgroundServiceHostingExtensionsTests.RegisterBackgroundServices_WhenStartupTaskThrows_ShouldNotStartBackgroundServices`

- **Use case**: startup failure blocks loop startup
- **Test kind**: Unit
- **Description**: one failing startup task and one background service
- **Behavior**: starts hosted service
- **Expected outcome**: startup exception is raised and background loop does not start
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/MicrosoftBackgroundServiceHostingExtensionsTests.cs`

#### `MicrosoftBackgroundServiceHostingExtensionsTests.RegisterBackgroundServices_WhenStartupTaskRegisteredFirst_ShouldCompleteStartupBeforeContinuousLoop`

- **Use case**: startup-before-loop ordering
- **Test kind**: Unit
- **Description**: recording startup task sets completion flag
- **Behavior**: starts hosted service and observes loop state
- **Expected outcome**: loop starts only after startup flag is set
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/MicrosoftBackgroundServiceHostingExtensionsTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| Full Generic Host startup with multiple startup tasks across several modules | Medium | Current tests focus on bridge and orchestrator behavior. |
| Automatic retry policy for failed startup tasks | Low | Not implemented in hosting core. |

### Out-of-Scope Use Cases

- Continuous processing loops (`IBackgroundService`).
- Application DDL migration policy beyond startup task hooks.

## Deep Docs

- [Hosted services](../../architecture/hosted-services.md)
- [PostgreSQL schema management](../../integrations/postgresql-schema-management.md)
