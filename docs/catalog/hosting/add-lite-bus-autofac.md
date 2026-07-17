# AddLiteBus (Autofac)

## Header

- **ID**: `hosting.add-lite-bus-autofac`
- **Name**: AddLiteBus (Autofac)
- **Maturity**: GA
- **Summary**: Composes LiteBus modules through `ContainerBuilder` and publishes a host manifest for Autofac hosts.

## What It Does

`ContainerBuilder.AddLiteBus(...)` is the Autofac composition entry point. Like the Microsoft DI adapter, it supports an advanced `Action<IModuleRegistry>` overload and a normal `Action<ILiteBusBuilder>` overload. It builds module order, executes module `Build(...)`, publishes `LiteBusHostManifest`, and wires diagnostic/background hosting bridges for Autofac.

The adapter also registers an `IServiceProvider` wrapper so factory registrations can resolve dependencies during composition.

## Public Surface

### Invocation

- `ContainerBuilder AddLiteBus(Action<IModuleRegistry> configureRegistry)`
- `ContainerBuilder AddLiteBus(Action<ILiteBusBuilder> configure)`

### Registration

- Registers `LiteBusHostManifest` as Autofac single instance.
- Applies `RegisterDiagnosticChecks(...)` from `LiteBus.Runtime.Extensions.Autofac.Hosting`.
- Applies `RegisterBackgroundServices(...)` from `LiteBus.Runtime.Extensions.Autofac.Hosting`.
- Registers `IServiceProvider` adapter backed by Autofac `ILifetimeScope`.
- Registers one Autofac-backed `IMessageDispatchScopeFactory` for mediation and durable processor scopes.

### Configuration

- `Action<IModuleRegistry>` for advanced module setup.
- `Action<ILiteBusBuilder>` for package-owned `Add*` feature extensions.

## Packages

- `LiteBus.Runtime.Extensions.Autofac`
- `LiteBus.Runtime.Extensions.Autofac.Hosting` (transitive hosting bridge registration)

## Requires

- `hosting.module-registry`
- `hosting.host-manifest`
- `hosting.autofac-hosting-bridge`

## Invariants

- Duplicate module registration throws configuration exception.
- Manifest output is created once and registered as singleton in the Autofac container.
- Each dispatch opens and disposes a child lifetime scope.

## Non-Goals

- Not a Microsoft DI adapter.
- Does not configure ASP.NET Core health endpoints or management endpoints.
- Does not configure OpenTelemetry exporters.

## Observability

No direct telemetry is emitted by this composition API. Probe and metrics behavior is handled by downstream hosting and telemetry capabilities.

## Test Coverage

### Covered Use Cases

#### `AutofacIntegrationTests.AddLiteBus_WithCommandModule_ResolvesAndExecutesHandlersCorrectly`

- **Use case**: command pipeline handlers resolve from Autofac after AddLiteBus composition
- **Test kind**: Unit
- **Description**: composes message and command modules, then resolves `ICommandMediator`
- **Behavior**: sends a command with pre/main/post handlers
- **Expected outcome**: all handlers execute in expected order
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/Autofac/AutofacIntegrationTests.cs`

#### `AutofacIntegrationTests.AddLiteBus_WithModuleRegistryOverload_ShouldRegisterLiteBusHostManifest`

- **Use case**: Autofac AddLiteBus publishes host manifest including diagnostic checks
- **Test kind**: Unit
- **Description**: registers inbox module with in-memory storage and diagnostic check
- **Behavior**: resolves `LiteBusHostManifest` from Autofac container
- **Expected outcome**: manifest contains expected diagnostic check name
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/Autofac/AutofacIntegrationTests.cs`

#### `AutofacHostManifestTests.AddLiteBus_should_register_lite_bus_host_manifest`

- **Use case**: base Autofac composition registers host manifest even without durable modules
- **Test kind**: Unit
- **Description**: composes message module only
- **Behavior**: checks `LiteBusHostManifest` registration
- **Expected outcome**: manifest registration exists
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/Autofac/AutofacHostManifestTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| Very large Autofac module graphs with deep dependency chains | Low | Core ordering is covered by module registry tests. |
| Autofac composition with all hosting extensions enabled in one integration host | Medium | Covered by pieces, not one end-to-end host test. |

### Out-of-Scope Use Cases

- Per-request authorization policy mapping for HTTP endpoints.
- Health endpoint behavior outside ASP.NET Core health and management bridges.

## Deep Docs

- [Hosted services](../../architecture/hosted-services.md)
- [Dependency graph](../../architecture/dependency-graph.md)
