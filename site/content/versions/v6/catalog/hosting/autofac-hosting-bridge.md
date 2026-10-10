# Autofac Hosting Bridge

## Header

- **ID**: `hosting.autofac-hosting-bridge`
- **Name**: Autofac hosting bridge
- **Maturity**: GA
- **Summary**: Maps manifest task/check descriptors to Autofac singleton registrations and one orchestrator `IHostedService`.

## What It Does

The Autofac hosting bridge mirrors Microsoft hosting behavior:

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
- All registered startup/background implementations are singletons.
- One orchestrator hosted service is registered for LiteBus-managed loops.

## Non-Goals

- Multi-container bridge support in one package.
- Built-in policy for container lifetime scopes beyond singleton host services.

## Observability

No bridge-specific telemetry. Runtime behavior is visible through the same diagnostics, management endpoints, and axis-specific metrics.

## Test Coverage

### Covered Use Cases

#### `AutofacBackgroundServiceHostingExtensionsTests.RegisterBackgroundServices_WhenImplementationTypeRepeated_ShouldResolveSingleHostedService`

- **Use case**: duplicate registration deduplication
- **Test kind**: Unit
- **Description**: registers same background type twice
- **Behavior**: resolves hosted services from container
- **Expected outcome**: one hosted service registration
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/AutofacBackgroundServiceHostingExtensionsTests.cs`

#### `AutofacBackgroundServiceHostingExtensionsTests.RegisterBackgroundServices_WhenBuilderNull_ShouldThrow`

- **Use case**: null guard
- **Test kind**: Unit
- **Description**: invokes extension with null builder
- **Behavior**: registration call
- **Expected outcome**: argument null exception
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/AutofacBackgroundServiceHostingExtensionsTests.cs`

#### `AutofacBackgroundServiceHostingExtensionsTests.RegisterBackgroundServices_ShouldExecuteUnderlyingBackgroundService`

- **Use case**: orchestrator execution through Autofac bridge
- **Test kind**: Unit
- **Description**: registers one background service and resolves hosted services
- **Behavior**: start and stop host
- **Expected outcome**: background execute count increments
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/AutofacBackgroundServiceHostingExtensionsTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| Dedicated tests for diagnostic-check registration dedup path in Autofac bridge | Low | Covered by composition tests that resolve manifest and checks. |

### Out-of-Scope Use Cases

- ASP.NET endpoint and auth mapping.

## Deep Docs

- [Hosted services](../../architecture/hosted-services.md)
