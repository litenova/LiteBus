# Composition Builder Surface

- **ID**: `runtime.litebus-builder`
- **Name**: Composition builder surface
- **Maturity**: GA
- **Summary**: Exposes the package-neutral module registry through `ILiteBusBuilder`.

## What It Does

`ILiteBusBuilder` is the root compose callback surface in `LiteBus.Runtime.Abstractions`. Its only member is `Modules`; installed feature packages add `AddMessaging`, `AddCommands`, `AddQueries`, `AddEvents`, `AddInbox`, `AddOutbox`, and root transport extensions. Advanced callers can use `Modules` directly.

## Public Surface

| API | Role |
| --- | --- |
| `ILiteBusBuilder.Modules` | `IModuleRegistry` access |
| `LiteBusBuilder(IModuleRegistry)` | Runtime implementation |
| Package-owned `Add*` extensions | Normal feature composition without runtime-to-feature references |

## Packages

- `LiteBus.Runtime.Abstractions` for the interface
- `LiteBus.Runtime` for the default implementation

## Requires

- `runtime.module-registry`

## Invariants

- Builder constructor rejects a null registry.
- Runtime does not reference Messaging or any feature package.
- Message contracts are registered on `AddMessaging`, `AddInbox`, or `AddOutbox` builders.

## Non-Goals

- Host adapter wiring.

## Observability

No dedicated telemetry.

## Test Coverage

### Covered Use Cases

#### `LiteBusBuilderTests.AddLiteBus_WithMessagingContracts_ShouldRegisterContractsInResolvedRegistry`
- **Test kind**: Unit
- **Expected outcome**: contracts registered through `AddMessaging` resolve from the live contract registry

#### `LiteBusBuilderTests.AddLiteBus_WithMultipleMessagingContracts_ShouldApplyAllRegistrations`
- **Test kind**: Unit
- **Expected outcome**: multiple module-owned registrations are applied together

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Very large module-local contract lists | Low | Functional registration is covered |

### Out-of-Scope Use Cases

- Builder mutation after container build.
