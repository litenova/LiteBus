# Composition Builder Surface

- **ID**: `runtime.litebus-builder`
- **Name**: Composition builder surface
- **Maturity**: GA
- **Summary**: The builder exposes the package-neutral module registry through `ILiteBusBuilder`.

## What It Does

`ILiteBusBuilder` is the root surface for composition callbacks, defined in `LiteBus.Runtime.Abstractions`. Its only member is `Modules`. Installed feature packages add `AddMessaging`, `AddCommands`, `AddQueries`, `AddEvents`, `AddInbox`, `AddOutbox`, and root transport extensions. Advanced callers can use `Modules` directly.

## Public Surface

| API | Role |
| --- | --- |
| `ILiteBusBuilder.Modules` | `IModuleRegistry` access |
| `LiteBusBuilder(IModuleRegistry)` | Runtime implementation |
| Package-owned `Add*` extensions | The normal way to compose features, without the runtime referencing feature packages |

## Packages

- `LiteBus.Runtime.Abstractions` for the interface
- `LiteBus.Runtime` for the default implementation

## Requires

- `runtime.module-registry`

## Invariants

- The builder constructor rejects a null registry.
- The runtime does not reference Messaging or any feature package.
- Message contracts are registered on the `AddMessaging`, `AddInbox`, or `AddOutbox` builders.

## Non-Goals

- Wiring host adapters.

## Observability

The builder emits no telemetry.

## Test Coverage

### Covered Use Cases

#### `LiteBusBuilderTests.AddLiteBus_WithMessagingContracts_ShouldRegisterContractsInResolvedRegistry`
- **Test kind**: Unit
- **Expected outcome**: contracts registered through `AddMessaging` resolve from the live contract registry

#### `LiteBusBuilderTests.AddLiteBus_WithMultipleMessagingContracts_ShouldApplyAllRegistrations`
- **Test kind**: Unit
- **Expected outcome**: the builder applies multiple module-owned registrations together

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Very large module-local contract lists | Low | Functional registration is covered |

### Out-of-Scope Use Cases

- Mutating the builder after the container is built.
