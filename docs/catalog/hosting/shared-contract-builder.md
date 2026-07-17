# Shared Contract Builder on `ILiteBusBuilder`

## Header

- **ID**: `hosting.shared-contract-builder`
- **Name**: Shared contract builder on `ILiteBusBuilder`
- **Maturity**: GA
- **Summary**: Allows cross-module contract registration once on `ILiteBusBuilder.Contracts`.

## What It Does

The `AddLiteBus(Action<ILiteBusBuilder>)` overload exposes `ILiteBusBuilder.Contracts` as a shared `IContractWriter`. These registrations are deferred and replayed when message modules build. This enables one composition callback to register contracts once for inbox, outbox, and transport paths.

## Public Surface

### Consumer Contracts

- `ILiteBusBuilder.Contracts` (`IContractWriter`)
- `ILiteBusBuilder.Modules` (`IModuleRegistry`)

### Registration

- Available in both Microsoft DI and Autofac `AddLiteBus(Action<ILiteBusBuilder>)` overloads.

## Packages

- `LiteBus.Runtime`
- `LiteBus.Messaging.Abstractions`

## Requires

- `hosting.add-lite-bus-microsoft-di` or `hosting.add-lite-bus-autofac`
- `runtime.contract-registry`

## Invariants

- Shared contracts are applied during module build, not at callback declaration time.
- Shared contracts and module-local contracts can coexist in one composition.

## Non-Goals

- Not a replacement for module-local contract registration when per-module ownership is required.
- Not an API for runtime contract mutation.

## Observability

No direct signals. Contract mismatches are surfaced at runtime by consumers resolving contract names and versions.

## Test Coverage

### Covered Use Cases

#### `LiteBusBuilderTests.AddLiteBus_WithSharedContracts_ShouldRegisterContractsInResolvedRegistry`

- **Use case**: shared contract registration path
- **Test kind**: Unit
- **Description**: registers shared contract through `builder.Contracts`
- **Behavior**: resolves contract registry after composition
- **Expected outcome**: configured contract resolves to expected type
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/LiteBusBuilderTests.cs`

#### `LiteBusBuilderTests.AddLiteBus_WithSharedAndModuleContracts_ShouldApplyBothWithoutConflict`

- **Use case**: mixed shared and module contracts
- **Test kind**: Unit
- **Description**: registers one shared and one module contract
- **Behavior**: resolves both contracts from registry
- **Expected outcome**: both mappings exist with expected versions
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/LiteBusBuilderTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| Conflict resolution when duplicate shared registrations point to different types | Medium | Expected behavior depends on registry rules in messaging axis. |

### Out-of-Scope Use Cases

- Runtime mutation of contracts after host start.

## Deep Docs

- [Hosted services](../../architecture/hosted-services.md)
- [Migration guide v6](../../migration/v6.md)
