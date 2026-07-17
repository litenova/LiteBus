# Generic Host Orchestrator

## Header

- **ID**: `hosting.generic-host-orchestrator`
- **Name**: Generic host orchestrator
- **Maturity**: GA
- **Summary**: Single `IHostedService` that runs startup tasks first, then background loops until shutdown.

## What It Does

`LiteBusHostOrchestrator` is an internal `IHostedService` used by both Microsoft DI and Autofac hosting bridges. On `StartAsync` it executes startup tasks sequentially. If startup succeeds, it starts background loops concurrently. On `StopAsync` it cancels loops and waits for completion.

Canceled loops during shutdown are treated as expected and suppressed.

## Public Surface

### Registration

- Orchestrator is registered by hosting bridges as one `IHostedService`.
- Startup/background implementations are resolved from manifest type lists.

### Behavior

- Startup failure aborts host startup.
- Background loops run concurrently with shared cancellation token.

## Packages

- `LiteBus.Runtime.Extensions.Hosting`

## Requires

- `hosting.startup-tasks`
- `hosting.background-services`
- `hosting.microsoft-hosting-bridge` or `hosting.autofac-hosting-bridge`

## Invariants

- Startup tasks always finish before background loops start.
- If no background services are registered, startup still completes successfully.
- Host stop requests linked cancellation for running loops.

## Non-Goals

- Per-loop crash isolation and restart strategy.
- Supervising non-LiteBus hosted services.

## Observability

No dedicated orchestrator telemetry is emitted. Failures surface through host startup/stop exceptions and loop-specific axis metrics.

## Test Coverage

### Covered Use Cases

#### `MicrosoftBackgroundServiceHostingExtensionsTests.RegisterBackgroundServices_WhenStartupTaskThrows_ShouldNotStartBackgroundServices`

- **Use case**: fail-closed startup
- **Test kind**: Unit
- **Description**: startup task throws before background service starts
- **Behavior**: starts hosted services
- **Expected outcome**: startup failure captured and background loop remains unstarted
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/MicrosoftBackgroundServiceHostingExtensionsTests.cs`

#### `MicrosoftBackgroundServiceHostingExtensionsTests.RegisterBackgroundServices_WhenStartupTaskRegisteredFirst_ShouldCompleteStartupBeforeContinuousLoop`

- **Use case**: startup-before-loop ordering
- **Test kind**: Unit
- **Description**: recording startup task and ordered background service
- **Behavior**: starts hosted services
- **Expected outcome**: background loop starts after startup completion flag
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/MicrosoftBackgroundServiceHostingExtensionsTests.cs`

#### `AutofacBackgroundServiceHostingExtensionsTests.RegisterBackgroundServices_ShouldExecuteUnderlyingBackgroundService`

- **Use case**: Autofac bridge executes orchestrator loop path
- **Test kind**: Unit
- **Description**: resolves hosted service from Autofac bridge registration
- **Behavior**: start/stop host
- **Expected outcome**: underlying loop execution count increases
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/AutofacBackgroundServiceHostingExtensionsTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| Multiple failing background loops and aggregate exception behavior | Medium | Current tests validate startup ordering and basic execution. |

### Out-of-Scope Use Cases

- Coordinating third-party background workloads.

## Deep Docs

- [Hosted services](../../architecture/hosted-services.md)
