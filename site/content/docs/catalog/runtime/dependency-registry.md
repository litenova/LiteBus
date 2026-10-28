# Container-Neutral Dependency Registry

- **ID**: `runtime.dependency-registry`
- **Name**: Container-neutral dependency registry
- **Maturity**: GA
- **Summary**: The registry tracks `DependencyDescriptor` entries, rejects conflicting registrations, and keeps single-service and collection registrations on separate paths.

## What It Does

`IDependencyRegistry` is the runtime registration abstraction used by modules. `DependencyRegistry` stores descriptors and rejects conflicting or mismatched registrations before container-specific adapters translate them.

## Public Surface

| API | Role |
| --- | --- |
| `Register(DependencyDescriptor)` | Single service registration |
| `RegisterCollection(DependencyDescriptor)` | Collection registration path |
| `DependencyRegistry` | Default implementation |

## Packages

- `LiteBus.Runtime`
- `LiteBus.Runtime.Abstractions`

## Requires

- `runtime.module-configuration`

## Invariants

- When two non-collection bindings conflict for the same service type, registration throws.
- Collection registrations require collection-marked descriptors.
- When a service is registered both singly and as collection items, composition fails.
- The registry ignores duplicate equivalent descriptors.

## Non-Goals

- Resolving services at run time.

## Observability

The registry emits no telemetry. Registration conflicts throw `ModuleCompositionException`.

## Test Coverage

### Covered Use Cases

#### `DependencyRegistryTests.Register_WithConflictingBindingForSameServiceType_ShouldThrowLiteBusConfigurationException`
- **Test kind**: Unit
- **Expected outcome**: the registry rejects the conflicting binding

#### `DependencyRegistryTests.RegisterCollection_ShouldAllowMultipleImplementations`
- **Test kind**: Unit
- **Expected outcome**: the registry accepts multiple collection items

#### `DependencyRegistryAdapterTests.MicrosoftAdapter_RegisterCollection_ShouldAllowMultipleImplementations`
- **Test kind**: Unit
- **Expected outcome**: the Microsoft DI adapter preserves collection semantics

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Concurrent parallel registration | Medium | Compose contract is single-threaded |

### Out-of-Scope Use Cases

- Replacing services after the container is built.
