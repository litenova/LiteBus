# Composition Builder Surface

- **ID**: `runtime.litebus-builder`
- **Name**: Composition builder surface
- **Maturity**: GA
- **Summary**: Exposes `Modules` and deferred `Contracts` registration through `ILiteBusBuilder`.

## What It Does

`ILiteBusBuilder` is the root compose callback surface. `Modules` registers runtime and feature modules. `Contracts` captures shared contract registrations and replays them when `MessageModule` builds.

## Public Surface

| API | Role |
| --- | --- |
| `ILiteBusBuilder.Modules` | `IModuleRegistry` access |
| `ILiteBusBuilder.Contracts` | Shared `IContractWriter` |
| `LiteBusBuilder(IModuleRegistry, MessageContractBuilder)` | Runtime implementation |

## Packages

- `LiteBus.Runtime`

## Requires

- `runtime.module-registry`
- `runtime.contract-registry`

## Invariants

- Builder constructor rejects null dependencies.
- Shared contract registrations are deferred until message module build.

## Non-Goals

- Host adapter wiring.

## Observability

No dedicated telemetry.

## Test Coverage

### Covered Use Cases

#### `LiteBusBuilderTests.AddLiteBus_WithSharedContracts_ShouldRegisterContractsInResolvedRegistry`
- **Test kind**: Unit
- **Expected outcome**: shared contracts are replayed into live contract registry

#### `LiteBusBuilderTests.AddLiteBus_WithSharedAndModuleContracts_ShouldApplyBothWithoutConflict`
- **Test kind**: Unit
- **Expected outcome**: both registration paths succeed when compatible

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Very large shared contract lists | Low | Functional replay is covered |

### Out-of-Scope Use Cases

- Builder mutation after container build.
