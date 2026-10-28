# Long-Running Background Services

## Header

- **ID**: `hosting.background-services`
- **Name**: Long-running background services
- **Maturity**: GA
- **Summary**: Manifest registration contract for continuous loops that run until host shutdown.

## What It Does

`IBackgroundService` defines the host-independent loop contract: `ExecuteAsync(CancellationToken)`. Modules register implementation types through `IModuleConfiguration.RegisterBackgroundService(Type)`. Host bridges resolve and run these through a single orchestrator.

Shipped loops are the inbox and outbox processor services with their cleanup services, the transport inbox ingress consumer, and one `RecurringCommandBackgroundService<TCommand>` per recurring registration. The recurring loop sends one command per pass on `RecurringCommandHostOptions`, survives failing passes with `ErrorBackoff`, and single-flights through `IRecurringCommandLease` when `RequireLease` is set; see [mediator.recurring-commands](../mediator/recurring-commands.md) and [Recurring commands](../../integrations/recurring-commands.md).

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

#### `RecurringCommandHostTests.Loop_sends_the_command_repeatedly`

- **Use case**: recurring loop sends one command per pass
- **Test kind**: Unit
- **Description**: registers a recurring command with a short interval
- **Behavior**: runs the loop and counts sends
- **Expected outcome**: the command is sent repeatedly until cancelled
- **Remarks**: `tests/LiteBus.Mediator.UnitTests/UseCases/Commands/RecurringCommandHostTests.cs`

#### `RecurringCommandHostTests.Loop_survives_a_failing_pass_and_keeps_sending`

- **Use case**: failing pass does not end the loop
- **Test kind**: Unit
- **Description**: registers a recurring command whose handler throws once
- **Behavior**: runs the loop past the failure
- **Expected outcome**: the loop logs, backs off, and keeps sending
- **Remarks**: `tests/LiteBus.Mediator.UnitTests/UseCases/Commands/RecurringCommandHostTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| Unified failure policy when one of multiple loops faults after startup | Medium | Covered indirectly by axis-specific processor tests. |

### Out-of-Scope Use Cases

- Per-loop retry strategy in the hosting layer.

## Deep Docs

- [Recurring commands](../../integrations/recurring-commands.md)
- [Hosted services](../../architecture/hosted-services.md)
