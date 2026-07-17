# Manual Startup and Loop Execution

## Header

- **ID**: `hosting.manual-host-execution`
- **Name**: Manual startup and loop execution
- **Maturity**: GA
- **Summary**: Supports direct execution of startup tasks and background loops without running full Generic Host orchestration.

## What It Does

After composition, callers can resolve startup-task and background-service implementations directly from DI and execute them manually. This is useful for integration tests, local tooling, and targeted smoke checks.

Manual mode bypasses orchestrator ordering guarantees unless callers enforce startup-before-loop sequencing.

## Public Surface

### Invocation

- Resolve `IStartupTask` implementation and call `RunAsync(...)`
- Resolve `IBackgroundService` implementation and call `ExecuteAsync(...)`

### Bridge Helper

- `GetHostedServiceForBackgroundService<TBackgroundService>()` can produce a host adapter wrapper for one background service in Microsoft DI test flows.

## Packages

- `LiteBus.Runtime.Abstractions`
- `LiteBus.Runtime.Extensions.Hosting`
- `LiteBus.Testing` (test helpers)

## Requires

- `hosting.add-lite-bus-microsoft-di` or `hosting.add-lite-bus-autofac`

## Invariants

- Manual mode does not auto-run startup tasks before loops.
- Root DI lifetimes still apply to resolved services.

## Non-Goals

- Production host lifecycle replacement.
- HTTP control plane for direct task/loop invocation.

## Observability

Manual execution emits the same axis-level metrics as hosted execution once loop code runs. No dedicated manual-mode instrumentation exists.

## Test Coverage

### Covered Use Cases

#### `ManagementEndpointPostgreSqlIntegrationTests.Health_IncludesRegisteredDiagnosticProbe`

- **Use case**: manually composed host exposes diagnostics without extra orchestrator APIs
- **Test kind**: Integration
- **Description**: starts test host with PostgreSQL storage and management endpoints
- **Behavior**: calls `/litebus/health` after composition
- **Expected outcome**: registered probe appears with healthy status
- **Remarks**: `tests/LiteBus.Extensions.IntegrationTests/AspNetCore/ManagementEndpointPostgreSqlIntegrationTests.cs`

#### `ManagementEndpointPostgreSqlIntegrationTests.QueryInboxMessages_ReturnsPersistedRows`

- **Use case**: composed host supports management reads without custom orchestrator interaction
- **Test kind**: Integration
- **Description**: accepts command into inbox and queries management route
- **Behavior**: calls `/litebus/inbox/messages`
- **Expected outcome**: persisted rows are returned
- **Remarks**: `tests/LiteBus.Extensions.IntegrationTests/AspNetCore/ManagementEndpointPostgreSqlIntegrationTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| Explicit direct invocation tests for resolved `IStartupTask.RunAsync(...)` | Medium | Current coverage is mostly through hosted integration flows. |
| Direct invocation tests for outbox `IBackgroundService.ExecuteAsync(...)` | Medium | Inbox loop direct behavior has broader unit test coverage than outbox manual invocation. |

### Out-of-Scope Use Cases

- Full production lifecycle without `IHost`.

## Deep Docs

- [Hosted services](../../architecture/hosted-services.md)
