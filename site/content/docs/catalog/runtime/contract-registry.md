# Message Contract Registry

- **ID**: `runtime.contract-registry`
- **Name**: Message contract registry
- **Maturity**: GA
- **Summary**: The registry maps message CLR types to stable contract names and versions, and maps stored contract keys back to message types.

## What It Does

`MessageContractRegistry` implements `IMessageContractRegistry` (`IContractWriter` and `IContractReader`). It supports explicit registration, assembly scanning for types marked with `[MessageContract]`, and lookup of message types from stored contract keys.

## Public Surface

| API | Role |
| --- | --- |
| `Register<TMessage>(string, int)` | Explicit generic contract registration |
| `Register(Type, string, int)` | Runtime type registration |
| `AddFromAssembly(Assembly)` | Attribute-driven registration |
| `RegisterFromAssembly(Assembly)` | Extension on `IContractWriter` and `IMessageContractRegistry` that delegates to `AddFromAssembly` |
| `GetContract(Type)` / `TryGetContract(Type)` | Type-to-contract lookup |
| `GetMessageType(string, int)` / `TryGetMessageType(string, int)` | Contract-to-type lookup |

## Packages

- `LiteBus.Messaging`
- `LiteBus.Messaging.Abstractions`

## Requires

- `runtime.message-module`

## Invariants

- Contract version defaults to 1 and must be positive.
- Explicit registration rejects open generic message types. Assembly scanning skips them.
- When a type or key mapping conflicts with an existing one, registration throws.

## Non-Goals

- Tools that migrate contract schemas.

## Observability

The registry emits no telemetry. Contract conflicts throw contract-specific exceptions.

## Test Coverage

### Covered Use Cases

#### `MessageContractRegistryTests.AddFromAssembly_ShouldRegisterAttributedClosedTypes`
- **Test kind**: Unit
- **Expected outcome**: the registry registers the attributed closed message types

#### `MessageContractRegistryTests.TryGetContract_WhenAttributePresent_ShouldRegisterOnDemand`
- **Test kind**: Unit
- **Expected outcome**: an attributed type registers itself on lookup

#### `LiteBusBuilderTests.AddLiteBus_WithMessagingContracts_ShouldRegisterContractsInResolvedRegistry`
- **Test kind**: Unit
- **Expected outcome**: the builder applies the messaging contract registrations

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| High-contention registration and lookup | Medium | Locking exists but contention tests are limited |

### Out-of-Scope Use Cases

- Synchronizing with an external schema registry.
