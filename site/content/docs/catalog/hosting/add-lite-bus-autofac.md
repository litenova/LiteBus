# AddLiteBus (Autofac)

## Header

- **ID**: `hosting.add-lite-bus-autofac`
- **Name**: AddLiteBus (Autofac)
- **Maturity**: GA
- **Summary**: Composes LiteBus modules through `ContainerBuilder` and publishes a host manifest for Autofac hosts.

## What It Does

`ContainerBuilder.AddLiteBus(...)` is the Autofac composition entry point. Its `Action<ILiteBusBuilder>` callback resolves the module build order and runs each module's `Build(...)`. It then publishes `LiteBusHostManifest` and wires the diagnostic and background hosting bridges for Autofac. Advanced callers register custom modules through `builder.Modules` inside the same callback.

The adapter also registers an `IServiceProvider` wrapper so factory registrations can resolve dependencies during composition.

## Public Surface

### Invocation

- `ContainerBuilder AddLiteBus(Action<ILiteBusBuilder> configure)`

### Registration

- Registers `LiteBusHostManifest` as a single instance in Autofac.
- Applies `RegisterDiagnosticChecks(...)` from `LiteBus.Runtime.Extensions.Autofac.Hosting`.
- Applies `RegisterBackgroundServices(...)` from `LiteBus.Runtime.Extensions.Autofac.Hosting`.
- Registers an `IServiceProvider` adapter backed by Autofac `ILifetimeScope`.
- Registers one Autofac-backed `IMessageDispatchScopeFactory` for mediation and durable processor scopes.

### Configuration

- An `Action<ILiteBusBuilder>` callback carrying package-owned `Add*` feature extensions and advanced `builder.Modules` access.

## Packages

- `LiteBus.Runtime.Extensions.Autofac`
- `LiteBus.Runtime.Extensions.Autofac.Hosting` (transitive hosting bridge registration)

## Requires

- `runtime.module-registry`
- `hosting.host-manifest`
- `hosting.autofac-hosting-bridge`

## Invariants

- A second `AddLiteBus` call on the same container builder throws `ModuleCompositionException`.
- Duplicate module registration throws a configuration exception.
- The manifest is created once and registered as a singleton in the Autofac container.
- Each dispatch opens and disposes a child lifetime scope.

## Non-Goals

- Not a Microsoft DI adapter.
- Does not configure ASP.NET Core health endpoints or management endpoints.
- Does not configure OpenTelemetry exporters.

## Observability

This composition API emits no telemetry directly. Downstream hosting and telemetry capabilities handle probe and metric behavior.

## Test Coverage

### Covered Use Cases

#### `AutofacIntegrationTests.AddLiteBus_WithCommandModule_ResolvesAndExecutesHandlersCorrectly`

- **Use case**: command pipeline handlers resolve from Autofac after AddLiteBus composition
- **Test kind**: Unit
- **Description**: composes message and command modules, then resolves `ICommandMediator`
- **Behavior**: sends a command with pre/main/post handlers
- **Expected outcome**: all handlers execute in expected order
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/Autofac/AutofacIntegrationTests.cs`

#### `AutofacIntegrationTests.AddLiteBus_WithBuilder_ShouldRegisterLiteBusHostManifest`

- **Use case**: Autofac AddLiteBus publishes host manifest including diagnostic checks
- **Test kind**: Unit
- **Description**: registers the inbox module with in-memory storage and a diagnostic check
- **Behavior**: resolves `LiteBusHostManifest` from Autofac container
- **Expected outcome**: manifest contains expected diagnostic check name
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/Autofac/AutofacIntegrationTests.cs`

#### `AutofacHostManifestTests.AddLiteBus_should_register_lite_bus_host_manifest`

- **Use case**: base Autofac composition registers host manifest even without durable modules
- **Test kind**: Unit
- **Description**: composes the message module only
- **Behavior**: checks `LiteBusHostManifest` registration
- **Expected outcome**: manifest registration exists
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/Autofac/AutofacHostManifestTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| Very large Autofac module graphs with deep dependency chains | Low | Module registry tests cover core ordering. |
| Autofac composition with all hosting extensions enabled in one integration host | Medium | Covered in part, but not by one end-to-end host test. |

### Out-of-Scope Use Cases

- Per-request authorization policy mapping for HTTP endpoints.
- Health endpoint behavior outside ASP.NET Core health and management bridges.

## References

- [Hosted services](../../architecture/hosted-services.md)
- [Dependency graph](../../architecture/dependency-graph.md)
