# Container-Neutral Dependency Registry

- **ID**: `runtime.dependency-registry`
- **Name**: Container-neutral dependency registry
- **Maturity**: GA
- **Summary**: Tracks `DependencyDescriptor` entries, blocks conflicting registrations, and supports explicit collection-registration semantics.

## What It Does

`IDependencyRegistry` is the runtime registration abstraction used by modules. `DependencyRegistry` stores descriptors and enforces rules before container-specific adapters translate registrations.

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

- Conflicting non-collection bindings for same service type fail.
- Collection registrations require collection-marked descriptors.
- Duplicate equivalent descriptors are ignored.

## Non-Goals

- Runtime service resolution.

## Observability

No direct telemetry. Registration conflicts throw `LiteBusConfigurationException`.

## Test Coverage

### Covered Use Cases

#### `DependencyRegistryTests.Register_WithConflictingBindingForSameServiceType_ShouldThrowLiteBusConfigurationException`
- **Test kind**: Unit
- **Expected outcome**: conflicting binding is rejected

#### `DependencyRegistryTests.RegisterCollection_ShouldAllowMultipleImplementations`
- **Test kind**: Unit
- **Expected outcome**: multiple collection items are accepted

#### `DependencyRegistryAdapterTests.MicrosoftAdapter_RegisterCollection_ShouldAllowMultipleImplementations`
- **Test kind**: Unit
- **Expected outcome**: Microsoft DI adapter preserves collection semantics

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Concurrent parallel registration | Medium | Compose contract is single-threaded |

### Out-of-Scope Use Cases

- Post-build service replacement.
