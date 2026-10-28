# Microsoft Hosting Bridge

## Header

- **ID**: `hosting.microsoft-hosting-bridge`
- **Name**: Microsoft hosting bridge
- **Maturity**: GA
- **Summary**: Maps manifest task and check descriptors to Microsoft DI registrations and one orchestrator `IHostedService`.

## What It Does

The Microsoft hosting bridge has two extension points:

- `RegisterDiagnosticChecks(...)`: registers each diagnostic check type as a singleton.
- `RegisterBackgroundServices(...)`: registers startup tasks, background services, `BackgroundServiceHostedServiceIndex`, and one orchestrator `IHostedService`.

It deduplicates implementation types while preserving first-seen order.

## Public Surface

### Registration

- `void RegisterDiagnosticChecks(this IServiceCollection services, IReadOnlyList<DiagnosticCheckDescriptor> diagnosticChecks)`
- `void RegisterBackgroundServices(this IServiceCollection services, IReadOnlyList<Type> startupTasks, IReadOnlyList<Type> backgroundServices)`
- `IHostedService GetHostedServiceForBackgroundService<TBackgroundService>(this IServiceProvider serviceProvider)` (manual adapter lookup for tests)

### Extension Points

- `BackgroundServiceHostedServiceIndex` provides lookup for the hosted wrapper of one background type.

## Packages

- `LiteBus.Runtime.Extensions.Microsoft.Hosting`

## Requires

- `hosting.host-manifest`
- `hosting.generic-host-orchestrator`

## Invariants

- No registration occurs when both startup and background lists are empty.
- Duplicate implementation types are skipped after the first registration.
- One orchestrator hosted service is registered for all LiteBus loops.

## Non-Goals

- Registering framework-agnostic probes outside DI.
- Hosting bridge for non-Microsoft DI containers.

## Observability

The bridge emits no metrics of its own. Hosted workloads and diagnostic endpoints reflect operational behavior.

## Test Coverage

### Covered Use Cases

#### `MicrosoftBackgroundServiceHostingExtensionsTests.RegisterBackgroundServices_WhenImplementationTypeRepeated_ShouldRegisterSingleHostedService`

- **Use case**: type deduplication in bridge registration
- **Test kind**: Unit
- **Description**: registers a repeated background type
- **Behavior**: inspects the service descriptors
- **Expected outcome**: one hosted service and one background implementation registration
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/MicrosoftBackgroundServiceHostingExtensionsTests.cs`

#### `MicrosoftBackgroundServiceHostingExtensionsTests.RegisterBackgroundServices_WhenServicesNull_ShouldThrow`

- **Use case**: null guard for service collection
- **Test kind**: Unit
- **Description**: invokes the extension with null services
- **Behavior**: registration call
- **Expected outcome**: argument null exception
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/MicrosoftBackgroundServiceHostingExtensionsTests.cs`

#### `MicrosoftBackgroundServiceHostingExtensionsTests.RegisterBackgroundServices_ShouldExecuteUnderlyingBackgroundService`

- **Use case**: hosted bridge executes resolved LiteBus loop
- **Test kind**: Unit
- **Description**: builds the provider with one background service through the bridge
- **Behavior**: starts and stops the hosted services
- **Expected outcome**: loop execute count increases
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/MicrosoftBackgroundServiceHostingExtensionsTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| `GetHostedServiceForBackgroundService<T>()` extension behavior in dedicated tests | Low | Execution tests exercise index wiring indirectly. |

### Out-of-Scope Use Cases

- ASP.NET authorization and endpoint policies.

## References

- [Hosted services](../../architecture/hosted-services.md)
