# Generic Host Orchestrator

## Header

- **ID**: `hosting.generic-host-orchestrator`
- **Name**: Generic host orchestrator
- **Maturity**: GA
- **Summary**: Supervised `BackgroundService` that runs startup tasks first, then background loops until shutdown.

## What It Does

`LiteBusHostOrchestrator` is an internal .NET Generic Host `BackgroundService` used by both Microsoft DI and Autofac hosting bridges. On `StartAsync` it executes startup tasks sequentially. If startup succeeds, it starts background loops concurrently. The base host contract cancels loops and waits for completion during shutdown.

Canceled loops during shutdown are treated as expected and suppressed. An unexpected loop fault calls `IHostApplicationLifetime.StopApplication()` immediately and remains faulted so the Generic Host can observe and log it.

## Public Surface

### Registration

- Orchestrator is registered by hosting bridges as one `IHostedService`.
- Startup/background implementations are resolved from manifest type lists.

### Behavior

- Startup failure aborts host startup.
- Background loops run concurrently with shared cancellation token.
- Unexpected background loop failure stops the application instead of leaving the host partially active.

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
- Every unexpected loop fault is fault-bearing and triggers fail-closed host shutdown.

## Non-Goals

- Per-loop restart strategy after an unexpected fault.
- Supervising non-LiteBus hosted services.

## Observability

No dedicated orchestrator telemetry is emitted. Failures surface through Generic Host background-service logging and loop-specific axis metrics.

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

#### `MicrosoftBackgroundServiceHostingExtensionsTests.RegisterBackgroundServices_WhenBackgroundServiceFaults_ShouldStopApplication`

- **Use case**: fail-closed Microsoft Generic Host supervision
- **Test kind**: Unit
- **Description**: one LiteBus loop faults while another remains active
- **Behavior**: observes the host lifetime stop signal
- **Expected outcome**: application shutdown is requested once
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/MicrosoftBackgroundServiceHostingExtensionsTests.cs`

#### `AutofacBackgroundServiceHostingExtensionsTests.RegisterBackgroundServices_WhenBackgroundServiceFaults_ShouldStopApplication`

- **Use case**: fail-closed Autofac Generic Host supervision
- **Test kind**: Unit
- **Description**: one LiteBus loop faults while another remains active
- **Behavior**: observes the host lifetime stop signal
- **Expected outcome**: application shutdown is requested once
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/AutofacBackgroundServiceHostingExtensionsTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| Multiple simultaneous loop faults and aggregate exception logging | Low | Each fault requests shutdown; tests cover the first-fault host signal. |

### Out-of-Scope Use Cases

- Coordinating third-party background workloads.

## Deep Docs

- [Hosted services](../../architecture/hosted-services.md)
