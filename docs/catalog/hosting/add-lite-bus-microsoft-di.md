# AddLiteBus (Microsoft DI)

## Header

- **ID**: `hosting.add-lite-bus-microsoft-di`
- **Name**: AddLiteBus (Microsoft DI)
- **Maturity**: GA
- **Summary**: Composes LiteBus modules through `IServiceCollection` and publishes a host manifest for hosting bridges.

## What It Does

`IServiceCollection.AddLiteBus(...)` is the Microsoft DI entry point for LiteBus composition. It accepts either `Action<IModuleRegistry>` or `Action<ILiteBusBuilder>`. Both overloads build module order, execute module `Build(...)`, register `LiteBusHostManifest`, and wire diagnostic checks and background hosting registrations.

This capability is composition only. It does not own ASP.NET routes, health checks, or exporters.

## Public Surface

### Invocation

- `IServiceCollection AddLiteBus(Action<IModuleRegistry> configureRegistry)`
- `IServiceCollection AddLiteBus(Action<ILiteBusBuilder> configure)`

### Registration

- Registers `LiteBusHostManifest` as singleton.
- Applies `RegisterDiagnosticChecks(...)` from `LiteBus.Runtime.Extensions.Microsoft.Hosting`.
- Applies `RegisterBackgroundServices(...)` from `LiteBus.Runtime.Extensions.Microsoft.Hosting`.

### Configuration

- `Action<IModuleRegistry>` for module-only setup.
- `Action<ILiteBusBuilder>` for shared contracts plus modules.

## Packages

- `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection`
- `LiteBus.Runtime.Extensions.Microsoft.Hosting` (transitive hosting bridge registration)

## Requires

- `hosting.module-registry`
- `hosting.host-manifest`
- `hosting.microsoft-hosting-bridge`

## Invariants

- Module build order is frozen after `BuildOrder()`.
- `LiteBusHostManifest` reflects startup tasks, background services, and diagnostic checks collected during composition.
- Shared contracts configured on `ILiteBusBuilder.Contracts` are replayed when message modules build.

## Non-Goals

- Not an Autofac adapter.
- Not a runtime endpoint or management API.
- Does not register OpenTelemetry exporters.

## Observability

No direct telemetry is emitted by `AddLiteBus`. Operational visibility comes from downstream capabilities:

- `hosting.host-manifest` for resolved manifest contents
- `hosting.aspnet-health-checks` and `hosting.aspnet-management-endpoints` for probe output
- `hosting.opentelemetry-*` for meter and trace registration

## Test Coverage

### Covered Use Cases

#### `LiteBusBuilderTests.AddLiteBus_WithSharedContracts_ShouldRegisterContractsInResolvedRegistry`

- **Use case**: shared contract registrations on the builder become available in resolved contract registry
- **Test kind**: Unit
- **Description**: configures `services.AddLiteBus(builder => ...)` with shared contracts and message module
- **Behavior**: resolves `IMessageContractRegistry` and looks up configured contract
- **Expected outcome**: contract name and version map to expected message type
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/LiteBusBuilderTests.cs`

#### `LiteBusBuilderTests.AddLiteBus_WithSharedAndModuleContracts_ShouldApplyBothWithoutConflict`

- **Use case**: shared and module-local contracts coexist in one composition
- **Test kind**: Unit
- **Description**: registers one shared contract and one module contract in a single builder callback
- **Behavior**: resolves registry and queries both contracts
- **Expected outcome**: both lookups succeed with expected types
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/LiteBusBuilderTests.cs`

#### `ModuleConfigurationDiagnosticCheckTests.AddLiteBus_ShouldRegisterLiteBusHostManifestWithDiagnosticChecks`

- **Use case**: manifest captures diagnostic checks from module builders
- **Test kind**: Unit
- **Description**: composes message plus inbox modules and registers a diagnostic check
- **Behavior**: resolves `LiteBusHostManifest`
- **Expected outcome**: manifest contains expected check type and name
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/ModuleConfigurationDiagnosticCheckTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| Full Microsoft Generic Host startup using only `AddLiteBus` as integration entry | Medium | Runtime hosting behavior is covered mostly through bridge-level tests. |
| AddLiteBus overload parity under very large module graphs | Low | Build order logic is tested independently in module registry tests. |

### Out-of-Scope Use Cases

- Runtime endpoint authorization and route policies.
- Exporter and sink configuration for observability backends.

## Deep Docs

- [Hosted services](../../architecture/hosted-services.md)
- [Diagnostics and health](../../operations/diagnostics-and-health.md)
