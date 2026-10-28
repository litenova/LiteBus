# Long-Running Background Services

## Header

- **ID**: `hosting.background-services`
- **Name**: Long-running background services
- **Maturity**: GA
- **Summary**: The manifest registration contract for continuous loops that run until host shutdown.

## What It Does

`IBackgroundService` defines the host-independent loop contract: `ExecuteAsync(CancellationToken)`. Modules register implementation types through `IModuleConfiguration.RegisterBackgroundService(Type)`. Host bridges resolve and run these through a single orchestrator.

Shipped loops are the inbox and outbox processor services with their cleanup services, the transport inbox ingress consumer, and one `RecurringCommandBackgroundService<TCommand>` per recurring registration. The recurring loop sends one command per pass, timed by `RecurringCommandHostOptions`. After a failing pass it waits `ErrorBackoff` instead of stopping. When `RequireLease` is set, the loop acquires an `IRecurringCommandLease` so the pass runs on at most one host instance at a time. See [mediator.recurring-commands](../mediator/recurring-commands.md) and [Recurring commands](../../integrations/recurring-commands.md).

## Public Surface

### Consumer Contracts

- `IBackgroundService.ExecuteAsync(CancellationToken)`
- `IModuleConfiguration.RegisterBackgroundService(Type)`
- `IModuleConfiguration.BackgroundServices`

### Registration

- Modules register loops during `Build(...)`.
- Hosting bridges resolve them as singleton implementations.

## Packages

- `LiteBus.Runtime.Abstractions`
- `LiteBus.Runtime` (module configuration implementation)

## Requires

- `hosting.generic-host-orchestrator`
- `hosting.microsoft-hosting-bridge` or `hosting.autofac-hosting-bridge`

## Invariants

- A type registered as a startup task cannot also be registered as a background service.
- Duplicate registrations are deduplicated while preserving first-seen order.
- Loops are canceled through the host shutdown token.

## Non-Goals

- Independent restart policy per background service.
- Scheduler/cron semantics.

## Observability

No generic background-service meter exists. The feature axes (inbox, outbox, ingress, transport) emit service-specific metrics.

## Test Coverage

### Covered Use Cases

#### `ModuleConfigurationTests.RegisterBackgroundService_ShouldPreserveFirstRegistrationOrderAndDeduplicate`

- **Use case**: deduplicated background-service registration
- **Test kind**: Unit
- **Description**: registers the same implementation twice
- **Behavior**: reads `BackgroundServices`
- **Expected outcome**: one entry in first-registration order
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/ModuleConfigurationTests.cs`

#### `ModuleConfigurationTests.RegisterBackgroundService_WhenTypeImplementsStartupTask_ShouldThrow`

- **Use case**: startup-task type blocked from background registration
- **Test kind**: Unit
- **Description**: attempts to register an `IStartupTask` implementation as a background service
- **Behavior**: calls `RegisterBackgroundService`
- **Expected outcome**: argument exception
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/ModuleConfigurationTests.cs`

#### `MicrosoftBackgroundServiceHostingExtensionsTests.RegisterBackgroundServices_ShouldExecuteUnderlyingBackgroundService`

- **Use case**: hosted adapter executes underlying LiteBus loop
- **Test kind**: Unit
- **Description**: registers one recording background service through the Microsoft bridge
- **Behavior**: starts and stops the hosted service
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
| Unified failure policy when one of multiple loops faults after startup | Medium | Axis-specific processor tests cover it indirectly. |

### Out-of-Scope Use Cases

- Per-loop retry strategy in the hosting layer.

## References

- [Recurring commands](../../integrations/recurring-commands.md)
- [Hosted services](../../architecture/hosted-services.md)
