# Module Dependency Ordering

- **ID**: `runtime.module-dependencies`
- **Name**: Module dependency ordering
- **Maturity**: GA
- **Summary**: Dependency edges come from `IRequires<TModule>` markers, read through `ModuleDescriptor.Create`.

## What It Does

Modules declare dependencies by implementing `IRequires<TModule>`. Descriptor creation reads these markers and provides dependency sets for the topological sort.

## Public Surface

| API | Role |
| --- | --- |
| `IRequires<TModule>` | Dependency marker |
| `ModuleDescriptor.Create(IModule)` | Extracts dependencies |
| `ModuleDescriptor.Dependencies` | Required module types |

## Packages

- `LiteBus.Runtime.Abstractions`
- `LiteBus.Runtime`

## Requires

- `runtime.modules`
- `runtime.module-registry`

## Invariants

- Dependencies are exact module types.
- A module can implement multiple `IRequires<>` markers.

## Non-Goals

- Negotiating optional dependencies.

## Observability

Dependency tracking emits no telemetry. Errors surface during registry ordering.

## Test Coverage

### Covered Use Cases

#### `ModuleRegistryTests.ModuleDescriptor_Create_ShouldCollectIRequiresDependencies`
- **Test kind**: Unit
- **Expected outcome**: the dependency set matches the markers

#### `ModuleRegistryTests.Enumerate_WithMissingRequiredModule_ShouldThrowLiteBusConfigurationException`
- **Test kind**: Unit
- **Expected outcome**: the registry rejects the missing dependency

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Large fan-out dependency graphs | Low | Not benchmarked |

### Out-of-Scope Use Cases

- Constraints on dependency versions.
