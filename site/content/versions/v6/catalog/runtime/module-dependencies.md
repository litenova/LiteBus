# Module Dependency Ordering

- **ID**: `runtime.module-dependencies`
- **Name**: Module dependency ordering
- **Maturity**: GA
- **Summary**: Uses `IRequires<TModule>` and `ModuleDescriptor.Create` to build dependency edges.

## What It Does

Modules declare dependencies by implementing `IRequires<TModule>`. Descriptor creation reflects these markers and provides dependency sets for topological sort.

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
- Multiple `IRequires<>` markers are supported.

## Non-Goals

- Optional dependency negotiation.

## Observability

No direct telemetry; errors surface during registry ordering.

## Test Coverage

### Covered Use Cases

#### `ModuleRegistryTests.ModuleDescriptor_Create_ShouldCollectIRequiresDependencies`
- **Test kind**: Unit
- **Expected outcome**: dependency set matches markers

#### `ModuleRegistryTests.Enumerate_WithMissingRequiredModule_ShouldThrowLiteBusConfigurationException`
- **Test kind**: Unit
- **Expected outcome**: missing dependency is rejected

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Large fan-out dependency graphs | Low | Not benchmarked |

### Out-of-Scope Use Cases

- Versioned dependency constraints.
