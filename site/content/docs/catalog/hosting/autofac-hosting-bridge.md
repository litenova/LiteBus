# Autofac Hosting Bridge

## Header

- **ID**: `hosting.autofac-hosting-bridge`
- **Name**: Autofac hosting bridge
- **Maturity**: GA
- **Summary**: Maps manifest task and check descriptors to Autofac singleton registrations and one orchestrator `IHostedService`.

## What It Does

The Autofac hosting bridge provides the same behavior as the Microsoft hosting bridge:

- `RegisterDiagnosticChecks(...)` registers unique probe types as singletons.
- `RegisterBackgroundServices(...)` registers startup and background service types as singletons and creates one `LiteBusHostOrchestrator` as `IHostedService`.

## Public Surface

### Registration

- `ContainerBuilder RegisterDiagnosticChecks(IReadOnlyList<DiagnosticCheckDescriptor>)`
- `ContainerBuilder RegisterBackgroundServices(IReadOnlyList<Type> startupTasks, IReadOnlyList<Type> backgroundServices)`

## Packages

- `LiteBus.Runtime.Extensions.Autofac.Hosting`

## Requires

- `hosting.host-manifest`
- `hosting.generic-host-orchestrator`

## Invariants

- Duplicate types are deduplicated before registration.
- All registered startup and background implementations are singletons.
- One orchestrator hosted service is registered for LiteBus-managed loops.

## Non-Goals

- Multi-container bridge support in one package.
- Built-in policy for container lifetime scopes beyond singleton host services.

## Observability

The bridge emits no telemetry of its own. The same diagnostics, management endpoints, and axis-specific metrics show runtime behavior.

## Test Coverage

### Covered Use Cases

#### `AutofacBackgroundServiceHostingExtensionsTests.RegisterBackgroundServices_WhenImplementationTypeRepeated_ShouldResolveSingleHostedService`

- **Use case**: duplicate registration deduplication
- **Test kind**: Unit
- **Description**: registers the same background type twice
- **Behavior**: resolves the hosted services from the container
- **Expected outcome**: one hosted service registration
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/AutofacBackgroundServiceHostingExtensionsTests.cs`

#### `AutofacBackgroundServiceHostingExtensionsTests.RegisterBackgroundServices_WhenBuilderNull_ShouldThrow`

- **Use case**: null guard
- **Test kind**: Unit
- **Description**: invokes the extension with a null builder
- **Behavior**: registration call
- **Expected outcome**: argument null exception
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/AutofacBackgroundServiceHostingExtensionsTests.cs`

#### `AutofacBackgroundServiceHostingExtensionsTests.RegisterBackgroundServices_ShouldExecuteUnderlyingBackgroundService`

- **Use case**: orchestrator execution through Autofac bridge
- **Test kind**: Unit
- **Description**: registers one background service and resolves the hosted services
- **Behavior**: starts and stops the host
- **Expected outcome**: background execute count increments
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/AutofacBackgroundServiceHostingExtensionsTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| Dedicated tests for diagnostic-check registration dedup path in Autofac bridge | Low | Composition tests that resolve the manifest and checks cover it. |

### Out-of-Scope Use Cases

- ASP.NET endpoint and auth mapping.

## References

- [Hosted services](../../architecture/hosted-services.md)
