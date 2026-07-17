# Host Manifest Snapshot

## Header

- **ID**: `hosting.host-manifest`
- **Name**: Host manifest snapshot
- **Maturity**: GA
- **Summary**: Immutable snapshot of startup tasks, background services, and diagnostic checks collected during module build.

## What It Does

`LiteBusHostManifest` is created from `IModuleConfiguration` after module build completes. It contains three collections:

- `StartupTasks`
- `BackgroundServices`
- `DiagnosticChecks`

Hosting bridges and management adapters consume this snapshot instead of scanning services ad hoc.

## Public Surface

### Consumer Contracts

- `LiteBusHostManifest`
- `LiteBusHostManifest.FromConfiguration(IModuleConfiguration)`

### Invocation

- Resolved from DI after `AddLiteBus(...)`.

## Packages

- `LiteBus.Runtime.Abstractions`
- Produced by composition adapters in `LiteBus.Runtime.Extensions.*`

## Requires

- `hosting.add-lite-bus-microsoft-di` or `hosting.add-lite-bus-autofac`

## Invariants

- Manifest contents represent first-registration order from module configuration.
- Manifest is a composition snapshot, not a mutable runtime registry.
- Diagnostic checks include both implementation type and declared descriptor name.

## Non-Goals

- Dynamic runtime updates when services change.
- Host-scoped filtering by environment or tenant.

## Observability

Manifest itself emits no telemetry. It is the source of truth for:

- probe execution in `DiagnosticCheckRunner`
- health and management endpoint reporting
- hosted orchestrator startup/background registration

## Test Coverage

### Covered Use Cases

#### `ModuleConfigurationDiagnosticCheckTests.AddLiteBus_ShouldRegisterLiteBusHostManifestWithDiagnosticChecks`

- **Use case**: manifest includes registered diagnostic checks
- **Test kind**: Unit
- **Description**: composes inbox module with one diagnostic check
- **Behavior**: resolves `LiteBusHostManifest` from provider
- **Expected outcome**: descriptor type and name match registration
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/ModuleConfigurationDiagnosticCheckTests.cs`

#### `AutofacHostManifestTests.AddLiteBus_should_register_lite_bus_host_manifest`

- **Use case**: Autofac host manifest registration
- **Test kind**: Unit
- **Description**: composes LiteBus with Autofac
- **Behavior**: queries Autofac container registrations
- **Expected outcome**: `LiteBusHostManifest` is registered
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/Autofac/AutofacHostManifestTests.cs`

#### `AutofacIntegrationTests.AddLiteBus_WithModuleRegistryOverload_ShouldRegisterLiteBusHostManifest`

- **Use case**: manifest contains diagnostic check in Autofac integration path
- **Test kind**: Unit
- **Description**: composes message/inbox modules and adds check
- **Behavior**: resolves manifest from container
- **Expected outcome**: expected diagnostic check descriptor exists
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/Autofac/AutofacIntegrationTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| Manifest serialization contract for external tooling | Low | Manifest is consumed in-memory by host adapters. |

### Out-of-Scope Use Cases

- Hot-reloading manifest entries while host is running.

## Deep Docs

- [Hosted services](../../architecture/hosted-services.md)
