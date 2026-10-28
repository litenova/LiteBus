# Module Registry and Build Order

- **ID**: `runtime.module-registry`
- **Name**: Module registry and build order
- **Maturity**: GA
- **Summary**: Registers modules, expands composites, computes topological order, and freezes after `BuildOrder()`.

## What It Does

`ModuleRegistry` implements `IModuleRegistry`. It rejects duplicate module types, computes dependency order from `IRequires<TModule>`, and throws on missing or circular dependencies.

Composite modules are expanded during registration through `ICompositeModule.DeclareChildren(...)`. This keeps child modules in the same dependency graph and ordering pass.

## Public Surface

| API | Role |
| --- | --- |
| `IModuleRegistry.Register(IModule)` | Registers module and composite children |
| `IModuleRegistry.IsModuleRegistered<T>()` | Exact type check |
| `IModuleRegistry.BuildOrder()` | Returns frozen dependency-ordered descriptors |

## Packages

- `LiteBus.Runtime`
- `LiteBus.Runtime.Abstractions`

## Requires

- `runtime.modules`
- `runtime.module-dependencies`

## Invariants

- Duplicate module type registration fails.
- Registration after `BuildOrder()` fails.
- Missing dependencies and cycles fail compose.
- Dependencies are built before dependents.

## Non-Goals

- DI adapter translation.
- Dynamic module registration after host start.
- Runtime plugin loading.

## Observability

No dedicated telemetry. Compose-time exceptions include explicit dependency diagnostics.

## Test Coverage

### Covered Use Cases

#### `ModuleRegistryTests.Register_DuplicateModuleType_ShouldThrowLiteBusConfigurationException`
- **Test kind**: Unit
- **Expected outcome**: duplicate module type is rejected

#### `ModuleRegistryTests.Enumerate_WithDependencyChain_ShouldOrderDependenciesFirst`
- **Test kind**: Unit
- **Expected outcome**: dependency-first order

#### `ModuleRegistryTests.Enumerate_WithCircularDependency_ShouldThrowLiteBusConfigurationException`
- **Test kind**: Unit
- **Expected outcome**: cycle detection exception

#### `ModuleRegistryTests.Enumerate_WithMissingRequiredModule_ShouldThrowLiteBusConfigurationException`
- **Test kind**: Unit
- **Description**: registers module requiring unregistered dependency
- **Expected outcome**: configuration exception naming missing dependency
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/ModuleRegistryTests.cs`

#### `ModuleRegistryTests.Register_AfterBuildOrder_ShouldThrowLiteBusConfigurationException`
- **Test kind**: Unit
- **Description**: builds order and then attempts new registration
- **Expected outcome**: configuration exception
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/ModuleRegistryTests.cs`

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| High-scale graph ordering benchmarks | Low | Functional ordering is covered |

### Out-of-Scope Use Cases

- Runtime graph mutation after freeze.
- Automatic dependency injection of missing modules.

## Deep Docs

- [Hosted services](../../architecture/hosted-services.md)
- [Architecture](../../architecture/README.md)
