# Message Contract Registry

- **ID**: `runtime.contract-registry`
- **Name**: Message contract registry
- **Maturity**: GA
- **Summary**: Maps message CLR types to stable contract name/version pairs and supports reverse lookup.

## What It Does

`MessageContractRegistry` implements `IMessageContractRegistry` (`IContractWriter` and `IContractReader`). It supports explicit registration, assembly scanning via `[MessageContract]`, and reverse type lookup from stored contract keys.

## Public Surface

| API | Role |
| --- | --- |
| `Register<TMessage>(string, int)` | Explicit generic contract registration |
| `Register(Type, string, int)` | Runtime type registration |
| `AddFromAssembly(Assembly)` | Attribute-driven registration |
| `GetContract(Type)` / `TryGetContract(Type)` | Type-to-contract lookup |
| `GetMessageType(string, int)` / `TryGetMessageType(string, int)` | Contract-to-type lookup |

## Packages

- `LiteBus.Messaging`
- `LiteBus.Messaging.Abstractions`

## Requires

- `runtime.message-module`

## Invariants

- Contract version must be positive.
- Open generic message types are rejected.
- Conflicting type or key mappings fail fast.

## Non-Goals

- Contract schema migration tooling.

## Observability

No dedicated telemetry. Contract conflicts throw contract-specific exceptions.

## Test Coverage

### Covered Use Cases

#### `MessageContractRegistryTests.AddFromAssembly_ShouldRegisterAttributedClosedTypes`
- **Test kind**: Unit
- **Expected outcome**: attributed closed message types are registered

#### `MessageContractRegistryTests.TryGetContract_WhenAttributePresent_ShouldRegisterOnDemand`
- **Test kind**: Unit
- **Expected outcome**: attributed type can self-register on lookup

#### `LiteBusBuilderTests.AddLiteBus_WithSharedContracts_ShouldRegisterContractsInResolvedRegistry`
- **Test kind**: Unit
- **Expected outcome**: deferred shared contracts are applied

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| High-contention registration and lookup | Medium | Locking exists, contention tests are limited |

### Out-of-Scope Use Cases

- External schema registry synchronization.
