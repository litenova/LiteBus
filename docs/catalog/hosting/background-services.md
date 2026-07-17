# Long-Running Background Services

## Header

- **ID**: `hosting.background-services`
- **Name**: Long-running background services
- **Maturity**: GA
- **Summary**: Manifest registration contract for continuous loops that run until host shutdown.

## What It Does

`IBackgroundService` defines the host-independent loop contract: `ExecuteAsync(CancellationToken)`. Modules register implementation types through `IModuleConfiguration.RegisterBackgroundService(Type)`. Host bridges resolve and run these through a single orchestrator.

Inbox/outbox processors and ingress consumers are the main implementations.

## Public Surface

### Consumer Contracts

- `IBackgroundService.ExecuteAsync(CancellationToken)`
- `IModuleConfiguration.RegisterBackgroundService(Type)`
- `IModuleConfiguration.BackgroundServices`

### Registration

- Registered during module `Build(...)`.
- Resolved by hosting bridges as singleton implementations.

## Packages

- `LiteBus.Runtime.Abstractions`
- `LiteBus.Runtime` (module configuration implementation)

## Requires

- `hosting.generic-host-orchestrator`
- `hosting.microsoft-hosting-bridge` or `hosting.autofac-hosting-bridge`

## Invariants

- Types registered as startup tasks cannot also register as background services.
- Duplicate registrations are deduplicated while preserving first-seen order.
- Loops are canceled through host shutdown token.

## Non-Goals

- Independent restart policy per background service.
- Scheduler/cron semantics.

## Observability

No generic background-service meter exists. Service-specific metrics are emitted by feature axes (inbox, outbox, ingress, transport).

## Test Coverage

### Covered Use Cases

#### `ModuleConfigurationTests.RegisterBackgroundService_ShouldPreserveFirstRegistrationOrderAndDeduplicate`

- **Use case**: deduplicated background-service registration
- **Test kind**: Unit
- **Description**: registers same implementation twice
- **Behavior**: reads `BackgroundServices`
- **Expected outcome**: one entry in first-registration order
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/ModuleConfigurationTests.cs`

#### `ModuleConfigurationTests.RegisterBackgroundService_WhenTypeImplementsStartupTask_ShouldThrow`

- **Use case**: startup-task type blocked from background registration
- **Test kind**: Unit
- **Description**: attempts to register an `IStartupTask` implementation as background service
- **Behavior**: calls `RegisterBackgroundService`
- **Expected outcome**: argument exception
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/ModuleConfigurationTests.cs`

#### `MicrosoftBackgroundServiceHostingExtensionsTests.RegisterBackgroundServices_ShouldExecuteUnderlyingBackgroundService`

- **Use case**: hosted adapter executes underlying LiteBus loop
- **Test kind**: Unit
- **Description**: registers one recording background service through Microsoft bridge
- **Behavior**: starts/stops hosted service
- **Expected outcome**: loop execute counter increments
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/MicrosoftBackgroundServiceHostingExtensionsTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| Unified failure policy when one of multiple loops faults after startup | Medium | Covered indirectly by axis-specific processor tests. |

### Out-of-Scope Use Cases

- Per-loop retry strategy in the hosting layer.

## Deep Docs

- [Hosted services](../../architecture/hosted-services.md)
