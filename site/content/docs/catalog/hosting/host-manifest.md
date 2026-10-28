# Host Manifest Snapshot

## Header

- **ID**: `hosting.host-manifest`
- **Name**: Host manifest snapshot
- **Maturity**: GA
- **Summary**: The immutable snapshot of startup tasks, background services, and diagnostic checks collected during module build.

## What It Does

`LiteBusHostManifest` is created from `IModuleConfiguration` after module build completes. It contains three collections:

- `StartupTasks`
- `BackgroundServices`
- `DiagnosticChecks`

Hosting bridges and management adapters consume this snapshot instead of scanning services themselves.

## Public Surface

### Consumer Contracts

- `LiteBusHostManifest`
- `LiteBusHostManifest.FromConfiguration(IModuleConfiguration)`

### Invocation

- Resolve it from DI after `AddLiteBus(...)`.

## Packages

- `LiteBus.Runtime.Abstractions`
- Composition adapters in `LiteBus.Runtime.Extensions.*` produce it

## Requires

- `hosting.add-lite-bus-microsoft-di` or `hosting.add-lite-bus-autofac`

## Invariants

- The manifest contents preserve the first-registration order from module configuration.
- The manifest is a composition snapshot, not a mutable runtime registry.
- Each diagnostic check carries both its implementation type and its declared descriptor name.

## Non-Goals

- Dynamic runtime updates when services change.
- Host-scoped filtering by environment or tenant.

## Observability

The manifest itself emits no telemetry. It provides the data for:

- probe execution in `DiagnosticCheckRunner`
- health and management endpoint reporting
- hosted orchestrator startup and background registration

## Test Coverage

### Covered Use Cases

#### `ModuleConfigurationDiagnosticCheckTests.AddLiteBus_ShouldRegisterLiteBusHostManifestWithDiagnosticChecks`

- **Use case**: manifest includes registered diagnostic checks
- **Test kind**: Unit
- **Description**: composes the inbox module with one diagnostic check
- **Behavior**: resolves `LiteBusHostManifest` from the provider
- **Expected outcome**: descriptor type and name match registration
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/ModuleConfigurationDiagnosticCheckTests.cs`

#### `AutofacHostManifestTests.AddLiteBus_should_register_lite_bus_host_manifest`

- **Use case**: Autofac host manifest registration
- **Test kind**: Unit
- **Description**: composes LiteBus with Autofac
- **Behavior**: queries Autofac container registrations
- **Expected outcome**: `LiteBusHostManifest` is registered
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/Autofac/AutofacHostManifestTests.cs`

#### `AutofacIntegrationTests.AddLiteBus_WithBuilder_ShouldRegisterLiteBusHostManifest`

- **Use case**: manifest contains diagnostic check in Autofac integration path
- **Test kind**: Unit
- **Description**: composes the message and inbox modules and adds a check
- **Behavior**: resolves the manifest from the container
- **Expected outcome**: expected diagnostic check descriptor exists
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/Autofac/AutofacIntegrationTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| Manifest serialization contract for external tooling | Low | Host adapters consume the manifest in memory. |

### Out-of-Scope Use Cases

- Hot-reloading manifest entries while host is running.

## References

- [Hosted services](../../architecture/hosted-services.md)
